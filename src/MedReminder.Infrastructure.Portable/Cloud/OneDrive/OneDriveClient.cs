using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace MedReminder.Infrastructure.Cloud.OneDrive;

// The app folder of a OneDrive account through Microsoft Graph (B.1 Phase
// 4a, docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §5.8, §18.6). Plain REST
// over HttpClient: no Graph SDK, so the same code runs on the mobile host.
// Paths are relative to the app folder, '/'-separated.
//
// Spike S6 findings this class relies on:
//   - PUT …/content with conflictBehavior=fail returns 409 on an existing
//     name, and creates missing parent folders;
//   - an upload session's item is listed from its first chunk, empty, and
//     deferCommit does not hide it: large files are uploaded under a
//     '.'-prefixed temporary name, then renamed with conflictBehavior;
//   - a rename onto an existing name with conflictBehavior=fail is 409.
//
// Access tokens come from the host (MSAL on the desktop); they are never
// logged, and error messages carry the HTTP status and Graph error code
// only.
public sealed class OneDriveClient
{
    // Graph's simple upload was documented up to 4 MB; larger files use an
    // upload session. Chunks must be multiples of 320 KiB.
    public const int SimpleUploadLimit = 4 * 1000 * 1000;
    internal const int ChunkSize = 320 * 1024 * 10;
    public const string TempPrefix = ".tmp-";

    private const string Drive = "https://graph.microsoft.com/v1.0/me/drive";
    private const int MaxAttempts = 5;

    private readonly HttpClient _http;
    private readonly Func<bool, CancellationToken, Task<string>> _token;
    private readonly TimeProvider _clock;

    // token(forceRefresh, ct): an access token with Files.ReadWrite.AppFolder.
    public OneDriveClient(HttpClient http, Func<bool, CancellationToken, Task<string>> token, TimeProvider? clock = null)
    {
        _http = http;
        _token = token;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<string> GetRootIdAsync(CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Drive}/special/approot?$select=id"), ct);
        await EnsureAsync(response, ct);
        return (await ReadJsonAsync(response, ct))["id"]!.GetValue<string>();
    }

    // Null when the file does not exist.
    public async Task<byte[]?> ReadAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Item(path)}/content"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken ct)
    {
        var bytes = await ReadAsync(path, ct);
        return bytes is null ? null : new MemoryStream(bytes, writable: false);
    }

    // Null when the name exists already (nothing is written).
    public Task<DriveItem?> CreateAsync(string path, byte[] content, CancellationToken ct)
        => UploadAsync(path, new MemoryStream(content, writable: false), content.Length, replace: false, ct);

    public async Task<DriveItem> WriteAsync(string path, byte[] content, CancellationToken ct)
        => (await UploadAsync(path, new MemoryStream(content, writable: false), content.Length, replace: true, ct))!;

    // The stream is read from its current position; `length` bytes.
    public async Task<DriveItem?> UploadAsync(string path, Stream content, long length, bool replace, CancellationToken ct)
    {
        ValidatePath(path);
        if (length <= SimpleUploadLimit)
        {
            var buffer = new byte[length];
            await content.ReadExactlyAsync(buffer, ct);
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put,
                $"{Item(path)}/content?@microsoft.graph.conflictBehavior={(replace ? "replace" : "fail")}")
            {
                Content = Octets(buffer, 0, buffer.Length),
            }, ct);
            if (!replace && response.StatusCode == HttpStatusCode.Conflict) return null;
            await EnsureAsync(response, ct);
            return DriveItem.Parse(await ReadJsonAsync(response, ct));
        }

        // Large file: temporary name, then rename (S6 C10b, C19).
        var slash = path.LastIndexOf('/');
        var folder = slash < 0 ? string.Empty : path[..(slash + 1)];
        var name = path[(slash + 1)..];
        var temp = await UploadSessionAsync($"{folder}{TempPrefix}{Guid.NewGuid():N}", content, length, ct);
        try
        {
            var body = new JsonObject
            {
                ["name"] = name,
                ["@microsoft.graph.conflictBehavior"] = replace ? "replace" : "fail",
            };
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Patch, $"{Drive}/items/{temp.Id}")
            {
                Content = Json(body),
            }, ct);
            if (!replace && response.StatusCode == HttpStatusCode.Conflict)
            {
                await DeleteItemAsync(temp.Id, ct);
                return null;
            }
            await EnsureAsync(response, ct);
            return DriveItem.Parse(await ReadJsonAsync(response, ct));
        }
        catch
        {
            await TryDeleteItemAsync(temp.Id);
            throw;
        }
    }

    // False when the file did not exist.
    public async Task<bool> DeleteAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, Item(path)), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureAsync(response, ct);
        return true;
    }

    public async Task DeleteItemAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{Drive}/items/{id}"), ct);
        if (response.StatusCode != HttpStatusCode.NotFound) await EnsureAsync(response, ct);
    }

    // Files and folders directly in `folder` ("" for the app folder); empty
    // when the folder does not exist.
    public async Task<IReadOnlyList<DriveItem>> ListChildrenAsync(string folder, CancellationToken ct)
    {
        var items = new List<DriveItem>();
        string? url = folder.Length == 0
            ? $"{Drive}/special/approot/children?$top=200"
            : $"{Item(folder.TrimEnd('/'))}/children?$top=200";
        while (url is not null)
        {
            var current = url;
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, current), ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return items;
            await EnsureAsync(response, ct);
            var page = await ReadJsonAsync(response, ct);
            items.AddRange(page["value"]!.AsArray().Select(v => DriveItem.Parse(v!)));
            url = page["@odata.nextLink"]?.GetValue<string>();
        }
        return items;
    }

    // One page of changes under the app folder. `link` is null for the
    // first page of a full enumeration, else a nextLink or deltaLink.
    // Throws DeltaExpiredException when the cursor is no longer valid.
    public async Task<DeltaPage> DeltaAsync(string? link, CancellationToken ct)
    {
        var url = link ?? $"{Drive}/special/approot/delta";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (response.StatusCode == HttpStatusCode.Gone) throw new DeltaExpiredException();
        await EnsureAsync(response, ct);
        var page = await ReadJsonAsync(response, ct);
        return new DeltaPage(
            [.. page["value"]!.AsArray().Select(v => DriveItem.Parse(v!))],
            page["@odata.nextLink"]?.GetValue<string>(),
            page["@odata.deltaLink"]?.GetValue<string>());
    }

    private async Task<DriveItem> UploadSessionAsync(string path, Stream content, long length, CancellationToken ct)
    {
        var body = new JsonObject { ["item"] = new JsonObject { ["@microsoft.graph.conflictBehavior"] = "fail" } };
        string uploadUrl;
        using (var session = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Item(path)}/createUploadSession")
               {
                   Content = Json(body),
               }, ct))
        {
            await EnsureAsync(session, ct);
            uploadUrl = (await ReadJsonAsync(session, ct))["uploadUrl"]!.GetValue<string>();
        }

        var buffer = new byte[ChunkSize];
        for (long offset = 0; offset < length;)
        {
            var size = (int)Math.Min(ChunkSize, length - offset);
            await content.ReadExactlyAsync(buffer.AsMemory(0, size), ct);
            var start = offset;
            // The upload URL is pre-authenticated: no Authorization header.
            using var response = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = Octets(buffer, 0, size) };
                request.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + size - 1, length);
                return request;
            }, ct, authorize: false);
            await EnsureAsync(response, ct);
            offset += size;
            if (offset >= length) return DriveItem.Parse(await ReadJsonAsync(response, ct));
        }
        throw new InvalidOperationException("Empty upload.");
    }

    private async Task TryDeleteItemAsync(string id)
    {
        try
        {
            await DeleteItemAsync(id, CancellationToken.None);
        }
        catch
        {
            // A leftover temporary file is ignored by readers ('.' prefix).
        }
    }

    // Retries throttling and transient server errors (Retry-After or
    // exponential back-off), and a rejected token once with a refresh.
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct, bool authorize = true)
    {
        var refreshed = false;
        for (var attempt = 1; ; attempt++)
        {
            using var request = build();
            if (authorize)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token(refreshed, ct));
            }
            var response = await _http.SendAsync(request, ct);
            if (authorize && response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                response.Dispose();
                refreshed = true;
                continue;
            }
            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                var wait = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - _clock.GetUtcNow() : (TimeSpan?)null)
                    ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                response.Dispose();
                if (wait > TimeSpan.Zero) await Task.Delay(Min(wait, TimeSpan.FromMinutes(2)), _clock, ct);
                continue;
            }
            return response;
        }
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? code = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            code = JsonNode.Parse(body)?["error"]?["code"]?.GetValue<string>();
        }
        catch
        {
            // Not a Graph error body.
        }
        throw new CloudStorageException(response.StatusCode, code);
    }

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
           ?? throw new CloudStorageException(response.StatusCode, "emptyBody");

    private static ByteArrayContent Octets(byte[] buffer, int offset, int count)
    {
        var content = new ByteArrayContent(buffer, offset, count);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static string Item(string path)
    {
        ValidatePath(path);
        return $"{Drive}/special/approot:/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}:";
    }

    // Relative, '/'-separated, no empty, '.' or '..' segment.
    public static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\\') || path.Split('/').Any(s => s.Length == 0 || s is "." or ".."))
        {
            throw new ArgumentException("Invalid remote path.", nameof(path));
        }
    }
}

public sealed record DriveItem(string Id, string? Name, string? ParentId, bool IsFolder, bool Deleted, long Size,
    DateTimeOffset? LastModified)
{
    internal static DriveItem Parse(JsonNode node) => new(
        node["id"]!.GetValue<string>(),
        node["name"]?.GetValue<string>(),
        node["parentReference"]?["id"]?.GetValue<string>(),
        node["folder"] is not null,
        node["deleted"] is not null,
        node["size"]?.GetValue<long>() ?? 0,
        node["lastModifiedDateTime"] is { } modified ? DateTimeOffset.Parse(modified.GetValue<string>(),
            System.Globalization.CultureInfo.InvariantCulture) : null);
}

public sealed record DeltaPage(IReadOnlyList<DriveItem> Items, string? NextLink, string? DeltaLink);

public sealed class DeltaExpiredException : Exception
{
    public DeltaExpiredException() : base("The OneDrive change cursor expired.")
    {
    }
}

// A failed provider request. The message carries the HTTP status and the
// provider's error code, never a token, a URL or content.
public sealed class CloudStorageException : IOException
{
    public CloudStorageException(HttpStatusCode status, string? code)
        : base($"OneDrive request failed: HTTP {(int)status}{(code is null ? string.Empty : $" ({code})")}.")
    {
        Status = status;
        Code = code;
    }

    public HttpStatusCode Status { get; }

    public string? Code { get; }
}
