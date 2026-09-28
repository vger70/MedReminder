using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MedReminder.Infrastructure.Cloud.OneDrive;

namespace MedReminder.Infrastructure.Cloud.GoogleDrive;

// Google Drive through the Drive REST API v3 (B.1 Phase 4b, docs/analysis/
// ANALYSIS-B1-MOBILE-SYNC.md §5.8, §18.7). Plain REST over HttpClient: no
// Google client library, so the same code runs on the mobile host.
// Drive addresses files by id, not by path, and allows duplicate names in
// a folder; the callers (GoogleDriveSyncTransport, GoogleDriveArchiveStorage)
// map their names to ids.
//
// Spike S7 findings this class relies on:
//   - a file created with a pre-generated id returns 409 when created
//     again with that id (C4b): a retried create is recognised;
//   - a resumable upload is not listed until its last chunk (C6b);
//   - files and properties are visible to every OAuth client of the
//     project (C12–C16).
//
// Access tokens come from the host; they are never logged, and error
// messages carry the HTTP status and Google's error reason only.
public sealed class GoogleDriveClient
{
    // Multipart uploads are documented up to 5 MB; larger files use a
    // resumable session, in chunks that are multiples of 256 KiB.
    public const int MultipartLimit = 5 * 1000 * 1000;
    internal const int ChunkSize = 256 * 1024 * 32;
    public const string FolderMimeType = "application/vnd.google-apps.folder";
    public const string AppDataFolder = "appDataFolder";

    private const string Api = "https://www.googleapis.com/drive/v3";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3";
    private const string Fields = "id,name,size,createdTime,modifiedTime,properties,parents,mimeType,trashed";
    private const int MaxAttempts = 5;

    private readonly HttpClient _http;
    private readonly Func<bool, CancellationToken, Task<string>> _token;
    private readonly TimeProvider _clock;

    // token(forceRefresh, ct): an access token with drive.file and drive.appdata.
    public GoogleDriveClient(HttpClient http, Func<bool, CancellationToken, Task<string>> token, TimeProvider? clock = null)
    {
        _http = http;
        _token = token;
        _clock = clock ?? TimeProvider.System;
    }

    // Every non-trashed file matching `query` in `space` ("drive" or
    // "appDataFolder"), all pages.
    public async Task<IReadOnlyList<GoogleFile>> QueryAsync(string query, string space, CancellationToken ct)
    {
        var files = new List<GoogleFile>();
        string? pageToken = null;
        do
        {
            var url = $"{Api}/files?q={Uri.EscapeDataString(query)}&spaces={space}&pageSize=1000"
                + $"&fields=nextPageToken,files({Fields})"
                + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            await EnsureAsync(response, ct);
            var page = await ReadJsonAsync(response, ct);
            files.AddRange(page["files"]!.AsArray().Select(f => GoogleFile.Parse(f!)));
            pageToken = page["nextPageToken"]?.GetValue<string>();
        }
        while (pageToken is not null);
        return files;
    }

    // Null when the file does not exist (or was deleted).
    public async Task<byte[]?> ReadAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files/{Id(id)}?alt=media"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // Streams the content; the caller disposes the stream. Null when the
    // file does not exist.
    public async Task<Stream?> OpenReadAsync(string id, CancellationToken ct)
    {
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files/{Id(id)}?alt=media"), ct,
            completion: HttpCompletionOption.ResponseHeadersRead);
        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }
            await EnsureAsync(response, ct);
            return new ResponseStream(response, await response.Content.ReadAsStreamAsync(ct));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<string> GenerateIdAsync(string space, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get,
            $"{Api}/files/generateIds?count=1&space={space}"), ct);
        await EnsureAsync(response, ct);
        return (await ReadJsonAsync(response, ct))["ids"]!.AsArray()[0]!.GetValue<string>();
    }

    public async Task<GoogleFile> CreateFolderAsync(string name, string parent, IReadOnlyDictionary<string, string> properties,
        CancellationToken ct)
    {
        var meta = Metadata(name, parent, properties, id: null);
        meta["mimeType"] = FolderMimeType;
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/files?fields={Fields}")
        {
            Content = Json(meta),
        }, ct);
        await EnsureAsync(response, ct);
        return GoogleFile.Parse(await ReadJsonAsync(response, ct));
    }

    // Creates a file with the given pre-generated id. A retry after a lost
    // response gets 409 for that id: the existing file is returned, since
    // only this caller holds the id.
    public async Task<GoogleFile> CreateAsync(string id, string name, string parent,
        IReadOnlyDictionary<string, string> properties, Stream content, long length, CancellationToken ct)
    {
        var meta = Metadata(name, parent, properties, id);
        HttpResponseMessage response;
        if (length <= MultipartLimit)
        {
            var buffer = new byte[length];
            await content.ReadExactlyAsync(buffer, ct);
            response = await SendAsync(() =>
            {
                var multipart = new MultipartContent("related") { Json(meta), Octets(buffer, 0, buffer.Length) };
                return new HttpRequestMessage(HttpMethod.Post, $"{Upload}/files?uploadType=multipart&fields={Fields}")
                {
                    Content = multipart,
                };
            }, ct);
        }
        else
        {
            response = await ResumableAsync(HttpMethod.Post, $"{Upload}/files?uploadType=resumable&fields={Fields}", meta,
                content, length, ct);
        }
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return await GetAsync(id, ct)
                    ?? throw new CloudStorageException(HttpStatusCode.Conflict, "idInUse", Provider);
            }
            await EnsureAsync(response, ct);
            return GoogleFile.Parse(await ReadJsonAsync(response, ct));
        }
    }

    public async Task<GoogleFile> UpdateContentAsync(string id, Stream content, long length, CancellationToken ct)
    {
        HttpResponseMessage response;
        if (length <= MultipartLimit)
        {
            var buffer = new byte[length];
            await content.ReadExactlyAsync(buffer, ct);
            response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Patch,
                $"{Upload}/files/{Id(id)}?uploadType=media&fields={Fields}")
            {
                Content = Octets(buffer, 0, buffer.Length),
            }, ct);
        }
        else
        {
            response = await ResumableAsync(HttpMethod.Patch, $"{Upload}/files/{Id(id)}?uploadType=resumable&fields={Fields}",
                new JsonObject(), content, length, ct);
        }
        using (response)
        {
            await EnsureAsync(response, ct);
            return GoogleFile.Parse(await ReadJsonAsync(response, ct));
        }
    }

    // Null when the file does not exist.
    public async Task<GoogleFile?> GetAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files/{Id(id)}?fields={Fields}"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureAsync(response, ct);
        var file = GoogleFile.Parse(await ReadJsonAsync(response, ct));
        return file.Trashed ? null : file;
    }

    // False when the file did not exist.
    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{Api}/files/{Id(id)}"), ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureAsync(response, ct);
        return true;
    }

    // Drive query literal: backslash and quote escaped.
    public static string Literal(string value) => "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal) + "'";

    public static string PropertyClause(string key, string value)
        => $"properties has {{ key={Literal(key)} and value={Literal(value)} }}";

    private const string Provider = "Google Drive";

    // Session URI, then chunks. Between chunks Drive answers 308 "Resume
    // Incomplete" without a Location header, which HttpClient returns as
    // is. The last chunk returns the file.
    private async Task<HttpResponseMessage> ResumableAsync(HttpMethod method, string url, JsonObject meta, Stream content,
        long length, CancellationToken ct)
    {
        var start = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(method, url) { Content = Json(meta) };
            request.Headers.Add("X-Upload-Content-Length", length.ToString(CultureInfo.InvariantCulture));
            return request;
        }, ct);
        // A create retried with its id: the caller handles the 409.
        if (start.StatusCode == HttpStatusCode.Conflict) return start;
        string session;
        using (start)
        {
            await EnsureAsync(start, ct);
            session = start.Headers.Location?.ToString()
                ?? throw new CloudStorageException(start.StatusCode, "noUploadSession", Provider);
        }

        var buffer = new byte[ChunkSize];
        for (long offset = 0; ;)
        {
            var size = (int)Math.Min(ChunkSize, length - offset);
            await content.ReadExactlyAsync(buffer.AsMemory(0, size), ct);
            var from = offset;
            var response = await SendAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, session) { Content = Octets(buffer, 0, size) };
                request.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, from + size - 1, length);
                return request;
            }, ct);
            offset += size;
            if ((int)response.StatusCode == 308 && offset < length)
            {
                response.Dispose();
                continue;
            }
            return response;
        }
    }

    // Retries throttling (429, 403 rate limits) and transient server
    // errors with exponential back-off, and a rejected token once with a
    // refresh.
    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, CancellationToken ct,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead)
    {
        var refreshed = false;
        for (var attempt = 1; ; attempt++)
        {
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _token(refreshed, ct));
            var response = await _http.SendAsync(request, completion, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                response.Dispose();
                refreshed = true;
                continue;
            }
            if (attempt < MaxAttempts && await IsTransientAsync(response, ct))
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                response.Dispose();
                if (wait > TimeSpan.Zero) await Task.Delay(wait < TimeSpan.FromMinutes(2) ? wait : TimeSpan.FromMinutes(2), _clock, ct);
                continue;
            }
            return response;
        }
    }

    private static async Task<bool> IsTransientAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError)
        {
            return true;
        }
        if (response.StatusCode != HttpStatusCode.Forbidden) return false;
        // Drive reports rate limits as 403 with a reason.
        await response.Content.LoadIntoBufferAsync(ct);
        return Reason(await response.Content.ReadAsStringAsync(ct)) is "rateLimitExceeded" or "userRateLimitExceeded";
    }

    private static string? Reason(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            return error?["errors"]?[0]?["reason"]?.GetValue<string>() ?? error?["status"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        throw new CloudStorageException(response.StatusCode, Reason(await response.Content.ReadAsStringAsync(ct)), Provider);
    }

    private static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
        => JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
           ?? throw new CloudStorageException(response.StatusCode, "emptyBody", Provider);

    private static JsonObject Metadata(string name, string parent, IReadOnlyDictionary<string, string> properties, string? id)
    {
        var props = new JsonObject();
        foreach (var (key, value) in properties) props[key] = value;
        var meta = new JsonObject
        {
            ["name"] = name,
            ["parents"] = new JsonArray(parent),
            ["properties"] = props,
        };
        if (id is not null) meta["id"] = id;
        return meta;
    }

    private static ByteArrayContent Octets(byte[] buffer, int offset, int count)
    {
        var content = new ByteArrayContent(buffer, offset, count);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static string Id(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Uri.EscapeDataString(id);
    }
}

public sealed record GoogleFile(string Id, string Name, long Size, DateTimeOffset CreatedTime, DateTimeOffset ModifiedTime,
    IReadOnlyDictionary<string, string> Properties, bool IsFolder, bool Trashed)
{
    internal static GoogleFile Parse(JsonNode node)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node["properties"] is JsonObject props)
        {
            foreach (var (key, value) in props)
            {
                if (value is not null) properties[key] = value.GetValue<string>();
            }
        }
        return new GoogleFile(
            node["id"]!.GetValue<string>(),
            node["name"]?.GetValue<string>() ?? string.Empty,
            // Drive returns sizes as strings (int64 in JSON).
            long.TryParse(node["size"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : 0,
            Time(node["createdTime"]),
            Time(node["modifiedTime"]),
            properties,
            node["mimeType"]?.GetValue<string>() == GoogleDriveClient.FolderMimeType,
            node["trashed"]?.GetValue<bool>() ?? false);
    }

    private static DateTimeOffset Time(JsonNode? node)
        => node is null ? DateTimeOffset.MinValue : DateTimeOffset.Parse(node.GetValue<string>(), CultureInfo.InvariantCulture);

    // Among files that share a name, the oldest wins, then the smallest id:
    // every device picks the same one.
    public static GoogleFile? Winner(IEnumerable<GoogleFile> files)
        => files.OrderBy(f => f.CreatedTime).ThenBy(f => f.Id, StringComparer.Ordinal).FirstOrDefault();
}
