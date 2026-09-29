using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace MedReminder.Infrastructure.Tests.Support;

// In-memory Microsoft Graph app folder for the OneDrive transport and
// archive tests (B.1 Phase 4a). It answers the requests OneDriveClient
// makes, with the behavior measured by spike S6 (§18.6):
//   - PUT content with conflictBehavior=fail is 409 on an existing name
//     and creates missing parents;
//   - an upload session's item is listed, empty, from createUploadSession
//     until the last chunk (C10b);
//   - PATCH rename with conflictBehavior=fail is 409 onto an existing
//     name (C19b);
//   - delete of a missing item is 404;
//   - delta returns changes since a cursor, in pages; a deleted folder is
//     reported without its children.
// Names compare case-insensitively, like OneDrive.
public sealed class FakeOneDrive : HttpMessageHandler
{
    private const string RootId = "approot";
    private const string DriveRootId = "driveroot";

    private readonly object _lock = new();
    private readonly Dictionary<string, Item> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private long _version;
    private int _nextId;

    public FakeOneDrive()
    {
        _items[RootId] = new Item(RootId, "MedReminder", DriveRootId, IsFolder: true) { Version = 0 };
    }

    public int DeltaPageSize { get; set; } = 2;

    public int ChildrenPageSize { get; set; } = 200;

    // Responses returned before the next request is served normally.
    public Queue<(HttpStatusCode Status, TimeSpan? RetryAfter)> Failures { get; } = new();

    public bool ExpireDeltaOnce { get; set; }

    public bool RejectNextToken { get; set; }

    // When false, delta does not report changes (feed lag).
    public bool DeltaReportsChanges { get; set; } = true;

    public List<string> Log { get; } = [];

    public List<string?> UploadAuthorizations { get; } = [];

    public HttpClient CreateClient() => new(this);

    // Test helpers ------------------------------------------------------

    public IReadOnlyList<string> Files()
    {
        lock (_lock)
        {
            return [.. _items.Values.Where(i => !i.IsFolder && !i.Deleted).Select(i => PathOf(i)!).Where(p => p is not null)
                .Order(StringComparer.Ordinal)];
        }
    }

    public byte[]? Content(string path)
    {
        lock (_lock)
        {
            return Find(path) is { IsFolder: false } item ? item.Content : null;
        }
    }

    public void Put(string path, byte[] content, DateTimeOffset? modified = null)
    {
        lock (_lock)
        {
            var item = Upsert(path);
            item.Content = content;
            if (modified is { } m) item.Modified = m;
        }
    }

    public void Remove(string path)
    {
        lock (_lock)
        {
            if (Find(path) is { } item) MarkDeleted(item);
        }
    }

    // HTTP --------------------------------------------------------------

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        lock (_lock)
        {
            var uri = request.RequestUri!;
            Log.Add($"{request.Method} {uri}");

            if (Failures.Count > 0)
            {
                var (status, retryAfter) = Failures.Dequeue();
                var failed = new HttpResponseMessage(status) { Content = Error("injected") };
                if (retryAfter is { } wait) failed.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
                return failed;
            }

            if (uri.Host == "upload.fake")
            {
                UploadAuthorizations.Add(request.Headers.Authorization?.ToString());
                return Upload(uri.AbsolutePath.Trim('/'), request, body);
            }

            if (request.Headers.Authorization is not { Scheme: "Bearer" } auth || string.IsNullOrEmpty(auth.Parameter))
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = Error("InvalidAuthenticationToken") };
            }
            if (RejectNextToken)
            {
                RejectNextToken = false;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = Error("InvalidAuthenticationToken") };
            }

            if (uri.Host == "graph.fake") return FakeLink(uri);
            return Graph(request.Method, uri, body);
        }
    }

    private HttpResponseMessage Graph(HttpMethod method, Uri uri, byte[] body)
    {
        const string prefix = "/v1.0/me/drive/";
        var path = uri.AbsolutePath;
        if (!path.StartsWith(prefix, StringComparison.Ordinal)) return Status(HttpStatusCode.BadRequest);
        path = path[prefix.Length..];
        var query = Query(uri);

        if (path.StartsWith("items/", StringComparison.Ordinal))
        {
            var id = path["items/".Length..];
            if (!_items.TryGetValue(id, out var byId) || byId.Deleted) return Status(HttpStatusCode.NotFound);
            if (method == HttpMethod.Delete)
            {
                MarkDeleted(byId);
                return Status(HttpStatusCode.NoContent);
            }
            if (method == HttpMethod.Patch) return Rename(byId, JsonNode.Parse(body)!, query);
            return Status(HttpStatusCode.MethodNotAllowed);
        }

        if (path == "special/approot") return Json(HttpStatusCode.OK, ToJson(_items[RootId]));
        if (path == "special/approot/delta") return Delta(0, 0);
        if (path == "special/approot/children") return Children(_items[RootId], 0);

        const string itemPrefix = "special/approot:/";
        if (!path.StartsWith(itemPrefix, StringComparison.Ordinal)) return Status(HttpStatusCode.BadRequest);
        var rest = path[itemPrefix.Length..];
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        var itemPath = string.Join('/', rest[..colon].Split('/').Select(Uri.UnescapeDataString));
        var action = rest[(colon + 1)..].TrimStart('/');
        var conflict = query.GetValueOrDefault("@microsoft.graph.conflictBehavior", "replace");
        var existing = Find(itemPath);

        switch (action, method.Method)
        {
            case ("", "GET"):
                return existing is null ? Status(HttpStatusCode.NotFound) : Json(HttpStatusCode.OK, ToJson(existing));
            case ("", "DELETE"):
                if (existing is null) return Status(HttpStatusCode.NotFound);
                MarkDeleted(existing);
                return Status(HttpStatusCode.NoContent);
            case ("content", "GET"):
                return existing is { IsFolder: false }
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(existing.Content) }
                    : Status(HttpStatusCode.NotFound);
            case ("content", "PUT"):
            {
                if (existing is not null && conflict == "fail") return Json(HttpStatusCode.Conflict, ErrorBody("nameAlreadyExists"));
                var created = existing is null;
                var item = Upsert(itemPath);
                item.Content = body;
                return Json(created ? HttpStatusCode.Created : HttpStatusCode.OK, ToJson(item));
            }
            case ("children", "GET"):
                return existing is { IsFolder: true } ? Children(existing, 0) : Status(HttpStatusCode.NotFound);
            case ("createUploadSession", "POST"):
            {
                var behavior = JsonNode.Parse(body)?["item"]?["@microsoft.graph.conflictBehavior"]?.GetValue<string>();
                if (existing is not null && behavior == "fail") return Json(HttpStatusCode.Conflict, ErrorBody("nameAlreadyExists"));
                // S6 C10b: the item is visible, empty, from now on.
                var item = Upsert(itemPath);
                item.Content = [];
                var id = $"s{++_nextId}";
                _sessions[id] = new Session(item.Id);
                return Json(HttpStatusCode.OK, new JsonObject { ["uploadUrl"] = $"https://upload.fake/{id}" });
            }
            default:
                return Status(HttpStatusCode.BadRequest);
        }
    }

    private HttpResponseMessage Upload(string sessionId, HttpRequestMessage request, byte[] body)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return Status(HttpStatusCode.NotFound);
        var range = request.Content!.Headers.ContentRange!;
        if (range.From != session.Received.Length) return Status(HttpStatusCode.RequestedRangeNotSatisfiable);
        session.Received = [.. session.Received, .. body];
        if (session.Received.Length < range.Length) return Json(HttpStatusCode.Accepted, new JsonObject());
        _sessions.Remove(sessionId);
        var item = _items[session.ItemId];
        item.Content = session.Received;
        Touch(item);
        return Json(HttpStatusCode.Created, ToJson(item));
    }

    private HttpResponseMessage Rename(Item item, JsonNode body, Dictionary<string, string> query)
    {
        var name = body["name"]!.GetValue<string>();
        var behavior = body["@microsoft.graph.conflictBehavior"]?.GetValue<string>()
            ?? query.GetValueOrDefault("@microsoft.graph.conflictBehavior", "fail");
        var sibling = Child(item.ParentId!, name);
        if (sibling is not null && sibling != item)
        {
            if (behavior == "fail") return Json(HttpStatusCode.Conflict, ErrorBody("nameAlreadyExists"));
            MarkDeleted(sibling);
        }
        item.Name = name;
        Touch(item);
        return Json(HttpStatusCode.OK, ToJson(item));
    }

    private HttpResponseMessage FakeLink(Uri uri)
    {
        var query = Query(uri);
        if (uri.AbsolutePath == "/delta")
        {
            var token = long.Parse(query["token"]);
            if (ExpireDeltaOnce && token > 0)
            {
                ExpireDeltaOnce = false;
                return Json(HttpStatusCode.Gone, ErrorBody("resyncRequired"));
            }
            return Delta(token, int.Parse(query.GetValueOrDefault("skip", "0")));
        }
        if (uri.AbsolutePath == "/children")
        {
            return Children(_items[query["parent"]], int.Parse(query["skip"]));
        }
        return Status(HttpStatusCode.BadRequest);
    }

    // token 0: full enumeration of live items; otherwise changes (tombstones
    // included) with a version above the token. The cursor handed out is
    // the version when the enumeration started.
    private HttpResponseMessage Delta(long token, int skip)
    {
        var changed = token == 0
            ? _items.Values.Where(i => !i.Deleted && (i.Id == RootId || PathOf(i) is not null))
            : DeltaReportsChanges
                ? _items.Values.Where(i => i.Version > token && (!i.Deleted || i.ReportDeletion))
                : [];
        var ordered = changed.OrderBy(i => i.Version).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();
        var page = ordered.Skip(skip).Take(DeltaPageSize).ToList();
        var result = new JsonObject { ["value"] = new JsonArray([.. page.Select(ToJson)]) };
        var cursor = DeltaReportsChanges ? _version : token;
        if (skip + page.Count < ordered.Count)
        {
            result["@odata.nextLink"] = $"https://graph.fake/delta?token={token}&skip={skip + page.Count}";
        }
        else
        {
            result["@odata.deltaLink"] = $"https://graph.fake/delta?token={cursor}";
        }
        return Json(HttpStatusCode.OK, result);
    }

    private HttpResponseMessage Children(Item folder, int skip)
    {
        var all = _items.Values.Where(i => !i.Deleted && i.ParentId == folder.Id).OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
        var page = all.Skip(skip).Take(ChildrenPageSize).ToList();
        var result = new JsonObject { ["value"] = new JsonArray([.. page.Select(ToJson)]) };
        if (skip + page.Count < all.Count)
        {
            result["@odata.nextLink"] = $"https://graph.fake/children?parent={folder.Id}&skip={skip + page.Count}";
        }
        return Json(HttpStatusCode.OK, result);
    }

    // Model -------------------------------------------------------------

    private Item? Find(string path)
    {
        var current = _items[RootId];
        foreach (var segment in path.Split('/'))
        {
            var child = Child(current.Id, segment);
            if (child is null) return null;
            current = child;
        }
        return current;
    }

    private Item? Child(string parentId, string name)
        => _items.Values.FirstOrDefault(i => !i.Deleted && i.ParentId == parentId
            && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    private Item Upsert(string path)
    {
        var current = _items[RootId];
        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var last = i == segments.Length - 1;
            var child = Child(current.Id, segments[i]);
            if (child is null)
            {
                child = new Item($"i{++_nextId}", segments[i], current.Id, IsFolder: !last);
                _items[child.Id] = child;
                Touch(child);
            }
            else if (last)
            {
                Touch(child);
            }
            current = child;
        }
        return current;
    }

    private void MarkDeleted(Item item)
    {
        // A deleted folder is reported alone; its children disappear with it.
        foreach (var child in _items.Values.Where(i => i.ParentId == item.Id && !i.Deleted).ToList())
        {
            MarkDeleted(child);
            child.ReportDeletion = false;
        }
        item.Deleted = true;
        item.ReportDeletion = true;
        Touch(item);
    }

    private void Touch(Item item)
    {
        item.Version = ++_version;
        item.Modified = DateTimeOffset.UtcNow;
    }

    private string? PathOf(Item item)
    {
        var segments = new List<string>();
        var current = item;
        while (current.Id != RootId)
        {
            if (current.Deleted || current.ParentId is null || !_items.TryGetValue(current.ParentId, out var parent)) return null;
            segments.Add(current.Name);
            current = parent;
        }
        segments.Reverse();
        return string.Join('/', segments);
    }

    private static JsonObject ToJson(Item item)
    {
        var json = new JsonObject
        {
            ["id"] = item.Id,
            ["parentReference"] = new JsonObject { ["id"] = item.ParentId },
            ["lastModifiedDateTime"] = item.Modified.ToString("O"),
        };
        if (item.Deleted)
        {
            json["deleted"] = new JsonObject { ["state"] = "deleted" };
            return json;
        }
        json["name"] = item.Name;
        json["size"] = item.Content.Length;
        json[item.IsFolder ? "folder" : "file"] = new JsonObject();
        return json;
    }

    private static Dictionary<string, string> Query(Uri uri)
        => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty);

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = Error(status.ToString()) };

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode body)
        => new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private static JsonObject ErrorBody(string code) => new() { ["error"] = new JsonObject { ["code"] = code } };

    private static StringContent Error(string code) => new(ErrorBody(code).ToJsonString(), Encoding.UTF8, "application/json");

    private sealed class Item(string id, string name, string? parentId, bool IsFolder)
    {
        public string Id { get; } = id;
        public string Name { get; set; } = name;
        public string? ParentId { get; } = parentId;
        public bool IsFolder { get; } = IsFolder;
        public byte[] Content { get; set; } = [];
        public bool Deleted { get; set; }
        public bool ReportDeletion { get; set; }
        public long Version { get; set; }
        public DateTimeOffset Modified { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class Session(string itemId)
    {
        public string ItemId { get; } = itemId;
        public byte[] Received { get; set; } = [];
    }
}
