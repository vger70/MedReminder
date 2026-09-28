using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MedReminder.Infrastructure.Tests.Support;

// In-memory Drive REST API v3 for the Google Drive transport and archive
// tests (B.1 Phase 4b). It answers the requests GoogleDriveClient makes,
// with the behavior measured by spike S7 (§18.7):
//   - duplicate names are allowed in a folder (C4);
//   - a create with an id that exists is 409 (C4b);
//   - a resumable upload is not listed before its last chunk (C6b);
//   - queries on public properties (C16).
// Queries understand the clauses the client writes: name =, 'id' in
// parents, properties has { key= and value= }, mimeType =, trashed = false.
// ListingLags hides files created or deleted afterwards from queries, as
// the few seconds of lag S7 measured (C8, C17); reads by id are immediate.
public sealed partial class FakeGoogleDrive : HttpMessageHandler
{
    public const string AppData = "appDataFolder";
    public const string Root = "root";

    private readonly object _lock = new();
    private readonly Dictionary<string, Item> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private int _next;
    private DateTimeOffset _time = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    public Queue<(HttpStatusCode Status, TimeSpan? RetryAfter)> Failures { get; } = new();

    public bool RejectNextToken { get; set; }

    // While true, queries show the state from when it was set: items with
    // a sequence up to the freeze point, deletions after it undone.
    public bool ListingLags
    {
        get => _lagSince is not null;
        set
        {
            lock (_lock) _lagSince = value ? _next : null;
        }
    }

    private int? _lagSince;

    public int PageSize { get; set; } = 1000;

    public List<string> Log { get; } = [];

    public HttpClient CreateClient() => new(this);

    // Test helpers --------------------------------------------------------

    // Names of live files in a space ("drive" or "appDataFolder").
    public IReadOnlyList<string> Names(string space)
    {
        lock (_lock)
        {
            return [.. _items.Values.Where(i => i.Live && !i.IsFolder && SpaceOf(i) == space).Select(i => i.Name)
                .Order(StringComparer.Ordinal)];
        }
    }

    public byte[]? ContentOf(string space, string name)
    {
        lock (_lock)
        {
            return _items.Values.FirstOrDefault(i => i.Live && SpaceOf(i) == space && i.Name == name)?.Content;
        }
    }

    // A file written by "another device", listed immediately.
    public string Put(string parent, string name, byte[] content, IReadOnlyDictionary<string, string>? properties = null,
        bool folder = false)
    {
        lock (_lock)
        {
            var item = NewItem(null, name, parent, folder, properties);
            item.Content = content;
            item.Uploaded = true;
            return item.Id;
        }
    }

    public string? FolderIdByProperty(string key, string value)
    {
        lock (_lock)
        {
            return _items.Values.FirstOrDefault(i => i.Live && i.IsFolder && i.Properties.GetValueOrDefault(key) == value)?.Id;
        }
    }

    public void Trash(string id)
    {
        lock (_lock) _items[id].Deleted = true;
    }

    // HTTP ----------------------------------------------------------------

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType;
        var range = request.Content?.Headers.ContentRange;
        lock (_lock)
        {
            var uri = request.RequestUri!;
            Log.Add($"{request.Method} {uri.AbsolutePath}{uri.Query}");
            if (Failures.Count > 0)
            {
                var (status, retryAfter) = Failures.Dequeue();
                var failed = Error(status, "injected");
                if (retryAfter is { } wait) failed.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
                return failed;
            }
            if (request.Headers.Authorization is not { Scheme: "Bearer" } auth || string.IsNullOrEmpty(auth.Parameter)
                || RejectNextToken)
            {
                RejectNextToken = false;
                return Error(HttpStatusCode.Unauthorized, "authError");
            }
            if (uri.Host == "upload.fake") return Chunk(uri.AbsolutePath.Trim('/'), range, body);

            var path = uri.AbsolutePath;
            var query = Query(uri);
            if (path.StartsWith("/upload/drive/v3/files", StringComparison.Ordinal))
            {
                return UploadRequest(request.Method, path["/upload/drive/v3/files".Length..].Trim('/'), query, body, contentType,
                    request.Headers.TryGetValues("X-Upload-Content-Length", out var lengths) ? long.Parse(lengths.First(), CultureInfo.InvariantCulture) : 0);
            }
            if (path == "/drive/v3/files/generateIds")
            {
                var ids = new JsonArray();
                for (var i = 0; i < int.Parse(query.GetValueOrDefault("count", "1"), CultureInfo.InvariantCulture); i++)
                {
                    ids.Add($"gen{++_next}");
                }
                return Json(HttpStatusCode.OK, new JsonObject { ["ids"] = ids });
            }
            if (path == "/drive/v3/files" && request.Method == HttpMethod.Get) return List(query);
            if (path == "/drive/v3/files" && request.Method == HttpMethod.Post)
            {
                var meta = JsonNode.Parse(body)!.AsObject();
                return Create(meta, [], complete: true);
            }
            if (path.StartsWith("/drive/v3/files/", StringComparison.Ordinal))
            {
                var id = Uri.UnescapeDataString(path["/drive/v3/files/".Length..]);
                if (!_items.TryGetValue(id, out var item) || !item.Live) return Error(HttpStatusCode.NotFound, "notFound");
                if (request.Method == HttpMethod.Delete)
                {
                    Delete(item);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (query.GetValueOrDefault("alt") == "media")
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(item.Content) };
                }
                return Json(HttpStatusCode.OK, ToJson(item));
            }
            return Error(HttpStatusCode.BadRequest, "badRequest");
        }
    }

    private HttpResponseMessage UploadRequest(HttpMethod method, string id, Dictionary<string, string> query, byte[] body,
        MediaTypeHeaderValue? contentType, long declaredLength)
    {
        var type = query.GetValueOrDefault("uploadType");
        if (method == HttpMethod.Post && type == "multipart")
        {
            var (meta, content) = Multipart(body, contentType!);
            return Create(meta, content, complete: true);
        }
        if (method == HttpMethod.Post && type == "resumable")
        {
            var meta = JsonNode.Parse(body)!.AsObject();
            if (meta["id"]?.GetValue<string>() is { } wanted && _items.ContainsKey(wanted)) return Error(HttpStatusCode.Conflict, "fileIdInUse");
            var session = $"s{++_next}";
            _sessions[session] = new Session(meta, null, declaredLength);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Location = new Uri($"https://upload.fake/{session}");
            return response;
        }
        if (method == HttpMethod.Patch && _items.TryGetValue(id, out var item) && item.Live)
        {
            if (type == "media")
            {
                item.Content = body;
                Touch(item);
                return Json(HttpStatusCode.OK, ToJson(item));
            }
            if (type == "resumable")
            {
                var session = $"s{++_next}";
                _sessions[session] = new Session(null, id, declaredLength);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Location = new Uri($"https://upload.fake/{session}");
                return response;
            }
        }
        return Error(HttpStatusCode.NotFound, "notFound");
    }

    private HttpResponseMessage Chunk(string sessionId, ContentRangeHeaderValue? range, byte[] body)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || range is null) return Error(HttpStatusCode.NotFound, "notFound");
        if (range.From != session.Received.Length) return Error(HttpStatusCode.BadRequest, "badRange");
        session.Received = [.. session.Received, .. body];
        if (session.Received.Length < session.Length) return new HttpResponseMessage((HttpStatusCode)308);
        _sessions.Remove(sessionId);
        if (session.UpdateId is { } id)
        {
            var item = _items[id];
            item.Content = session.Received;
            Touch(item);
            return Json(HttpStatusCode.OK, ToJson(item));
        }
        // S7 C6b: the file exists only once the last chunk arrived.
        return Create(session.Meta!, session.Received, complete: true);
    }

    private HttpResponseMessage Create(JsonObject meta, byte[] content, bool complete)
    {
        var id = meta["id"]?.GetValue<string>();
        if (id is not null && _items.ContainsKey(id)) return Error(HttpStatusCode.Conflict, "fileIdInUse");
        var parent = meta["parents"]?.AsArray().FirstOrDefault()?.GetValue<string>() ?? Root;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        if (meta["properties"] is JsonObject props)
        {
            foreach (var (k, v) in props) properties[k] = v!.GetValue<string>();
        }
        var folder = meta["mimeType"]?.GetValue<string>() == "application/vnd.google-apps.folder";
        var item = NewItem(id, meta["name"]!.GetValue<string>(), parent, folder, properties);
        item.Content = content;
        item.Uploaded = complete;
        return Json(HttpStatusCode.OK, ToJson(item));
    }

    private HttpResponseMessage List(Dictionary<string, string> query)
    {
        var space = query.GetValueOrDefault("spaces", "drive");
        var q = query.GetValueOrDefault("q", string.Empty);
        var filters = new List<Func<Item, bool>>();
        foreach (Match m in PropertyClause().Matches(q))
        {
            var (key, value) = (Unquote(m.Groups["k"].Value), Unquote(m.Groups["v"].Value));
            filters.Add(i => i.Properties.GetValueOrDefault(key) == value);
        }
        foreach (var clause in PropertyClause().Replace(q, "true").Split(" and "))
        {
            var c = clause.Trim();
            if (c is "true" or "trashed = false" or "") continue;
            if (NameClause().Match(c) is { Success: true } n) { var name = Unquote(n.Groups["v"].Value); filters.Add(i => i.Name == name); continue; }
            if (MimeClause().Match(c) is { Success: true } mt) { var mime = Unquote(mt.Groups["v"].Value); filters.Add(i => (i.IsFolder ? "application/vnd.google-apps.folder" : "application/octet-stream") == mime); continue; }
            if (ParentClause().Match(c) is { Success: true } p) { var parent = Unquote(p.Groups["v"].Value); filters.Add(i => i.Parent == parent || (parent == Root && i.Parent == Root)); continue; }
            return Error(HttpStatusCode.BadRequest, "unsupportedQuery");
        }

        var visible = _items.Values
            .Where(i => _lagSince is { } since ? i.Seq <= since && (!i.Deleted || i.DeletedSeq > since) : i.Live)
            .Where(i => i.Uploaded && SpaceOf(i) == space)
            .Where(i => filters.All(f => f(i)))
            .OrderBy(i => i.Seq)
            .ToList();
        var skip = int.Parse(query.GetValueOrDefault("pageToken", "0"), CultureInfo.InvariantCulture);
        var page = visible.Skip(skip).Take(PageSize).ToList();
        var result = new JsonObject { ["files"] = new JsonArray([.. page.Select(ToJson)]) };
        if (skip + page.Count < visible.Count) result["nextPageToken"] = (skip + page.Count).ToString(CultureInfo.InvariantCulture);
        return Json(HttpStatusCode.OK, result);
    }

    // Model ---------------------------------------------------------------

    private Item NewItem(string? id, string name, string parent, bool folder, IReadOnlyDictionary<string, string>? properties)
    {
        var item = new Item(id ?? $"f{++_next}", name, parent, folder)
        {
            Seq = ++_next,
            Created = _time = _time.AddSeconds(1),
        };
        foreach (var (k, v) in properties ?? new Dictionary<string, string>()) item.Properties[k] = v;
        _items[item.Id] = item;
        return item;
    }

    private void Delete(Item item)
    {
        foreach (var child in _items.Values.Where(i => i.Parent == item.Id && i.Live).ToList()) Delete(child);
        item.Deleted = true;
        item.DeletedSeq = ++_next;
    }

    private void Touch(Item item) => item.Modified = _time = _time.AddSeconds(1);

    private string SpaceOf(Item item)
    {
        var current = item;
        for (var depth = 0; depth < 32; depth++)
        {
            if (current.Parent == AppData) return AppData;
            if (current.Parent == Root || !_items.TryGetValue(current.Parent, out var parent)) return "drive";
            current = parent;
        }
        return "drive";
    }

    private static JsonObject ToJson(Item item)
    {
        var props = new JsonObject();
        foreach (var (k, v) in item.Properties) props[k] = v;
        return new JsonObject
        {
            ["id"] = item.Id,
            ["name"] = item.Name,
            ["size"] = item.Content.Length.ToString(CultureInfo.InvariantCulture),
            ["createdTime"] = item.Created.ToString("O", CultureInfo.InvariantCulture),
            ["modifiedTime"] = (item.Modified ?? item.Created).ToString("O", CultureInfo.InvariantCulture),
            ["properties"] = props,
            ["parents"] = new JsonArray(item.Parent),
            ["mimeType"] = item.IsFolder ? "application/vnd.google-apps.folder" : "application/octet-stream",
            ["trashed"] = false,
        };
    }

    // multipart/related: JSON metadata, then the content.
    private static (JsonObject Meta, byte[] Content) Multipart(byte[] body, MediaTypeHeaderValue type)
    {
        var boundary = type.Parameters.First(p => p.Name == "boundary").Value!.Trim('"');
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var parts = new List<byte[]>();
        var index = IndexOf(body, delimiter, 0);
        while (index >= 0)
        {
            var start = index + delimiter.Length;
            if (start + 1 < body.Length && body[start] == '-' && body[start + 1] == '-') break;
            var next = IndexOf(body, delimiter, start);
            if (next < 0) break;
            var part = body[start..next];
            var headerEnd = IndexOf(part, "\r\n\r\n"u8.ToArray(), 0);
            var content = part[(headerEnd + 4)..];
            if (content.Length >= 2 && content[^2] == '\r' && content[^1] == '\n') content = content[..^2];
            parts.Add(content);
            index = next;
        }
        return (JsonNode.Parse(parts[0])!.AsObject(), parts[1]);
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        var at = haystack.AsSpan(from).IndexOf(needle);
        return at < 0 ? -1 : at + from;
    }

    private static Dictionary<string, string> Query(Uri uri)
        => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty);

    private static string Unquote(string literal)
        => literal[1..^1].Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    [GeneratedRegex(@"properties has \{ key=(?<k>'(?:[^'\\]|\\.)*') and value=(?<v>'(?:[^'\\]|\\.)*') \}")]
    private static partial Regex PropertyClause();

    [GeneratedRegex(@"^name = (?<v>'(?:[^'\\]|\\.)*')$")]
    private static partial Regex NameClause();

    [GeneratedRegex(@"^mimeType = (?<v>'(?:[^'\\]|\\.)*')$")]
    private static partial Regex MimeClause();

    [GeneratedRegex(@"^(?<v>'(?:[^'\\]|\\.)*') in parents$")]
    private static partial Regex ParentClause();

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body)
        => new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Error(HttpStatusCode status, string reason)
        => Json(status, new JsonObject
        {
            ["error"] = new JsonObject
            {
                ["code"] = (int)status,
                ["errors"] = new JsonArray(new JsonObject { ["reason"] = reason }),
            },
        });

    private sealed class Item(string id, string name, string parent, bool isFolder)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public string Parent { get; } = parent;
        public bool IsFolder { get; } = isFolder;
        public Dictionary<string, string> Properties { get; } = new(StringComparer.Ordinal);
        public byte[] Content { get; set; } = [];
        public bool Uploaded { get; set; }
        public bool Deleted { get; set; }
        public int Seq { get; set; }
        public int DeletedSeq { get; set; } = int.MaxValue;
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset? Modified { get; set; }
        public bool Live => !Deleted;
    }

    private sealed class Session(JsonObject? meta, string? updateId, long length)
    {
        public JsonObject? Meta { get; } = meta;
        public string? UpdateId { get; } = updateId;
        public long Length { get; } = length;
        public byte[] Received { get; set; } = [];
    }
}
