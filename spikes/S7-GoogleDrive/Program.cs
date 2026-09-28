// Spike S7 (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §13 Phase 0, §5.8):
// what Google Drive offers the sync transport of Phase 4b, through the
// Drive REST API v3 with a hand-written OAuth flow (loopback redirect,
// PKCE), as the app would do it. Throw-away code: not part of
// MedReminder.sln; results go to §18.7 of the analysis.
//
// The main question: are files created by one OAuth client visible to
// another client of the same Cloud project (desktop vs Android), with
// drive.file and with drive.appdata? Two Desktop clients of one project
// stand in for the desktop and the Android client.
//
// It writes only its own test files in Drive (deleted at the end unless
// --keep) and, for token caches and the report, under
// %LOCALAPPDATA%\MedReminder\spike-s7\. It never prints tokens, client
// secrets, the account e-mail or file ids.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace S7.GoogleDrive;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null)
        {
            Console.WriteLine(Options.Usage);
            return 2;
        }

        var workDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedReminder", "spike-s7");
        Directory.CreateDirectory(workDir);
        var report = new Report(options);
        try
        {
            await RunAsync(options, workDir, report);
        }
        catch (Exception ex)
        {
            report.Add("ABORT", "Unhandled", $"{ex.GetType().Name}: {Short(ex.Message)}");
        }

        var path = Path.Combine(workDir, $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.md");
        await File.WriteAllTextAsync(path, report.ToMarkdown());
        Console.WriteLine();
        Console.WriteLine($"Report written to {path}");
        return report.Failed ? 1 : 0;
    }

    private static async Task RunAsync(Options options, string workDir, Report report)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        // --- Client A: sign-in and refresh ---------------------------------
        var a = new OAuthClient("A", options.ClientAId, options.ClientASecret, Path.Combine(workDir, "token-a.protected"), http, report);
        await a.SignInAsync();
        var driveA = new Drive(http, a, report);

        var run = $"MedReminder-S7-{DateTime.UtcNow:yyyyMMddHHmmss}";

        // --- drive.file space ------------------------------------------------
        var sw = Stopwatch.StartNew();
        var root = await driveA.CreateFolderAsync(run, null);
        var sync = root is null ? null : await driveA.CreateFolderAsync("sync", root);
        var ops = sync is null ? null : await driveA.CreateFolderAsync("ops", sync);
        report.Add(ops is not null ? "PASS" : "FAIL", "C2 Folders in My Drive (drive.file)",
            $"3 nested folders in {sw.ElapsedMilliseconds} ms (one request each: Drive has no path addressing).");
        if (root is null || ops is null) return;

        var small = RandomNumberGenerator.GetBytes(2048);
        var (st1, f1) = await driveA.CreateFileAsync("1.mrs", ops, small);
        report.Add(st1 == HttpStatusCode.OK && f1 is not null ? "PASS" : "FAIL", "C3 Multipart create of a small file",
            $"HTTP {(int)st1}.");

        var (readSt, readBytes) = await driveA.ReadAsync(f1!);
        report.Add(readSt == HttpStatusCode.OK && readBytes.AsSpan().SequenceEqual(small) ? "PASS" : "FAIL", "C3b Read back",
            $"HTTP {(int)readSt}, equal: {readBytes.AsSpan().SequenceEqual(small)}.");

        // Create-only: Drive allows duplicate names.
        var (st2, f2) = await driveA.CreateFileAsync("1.mrs", ops, RandomNumberGenerator.GetBytes(16));
        var dupes = await driveA.ListAsync($"'{ops}' in parents and name = '1.mrs' and trashed = false", "drive");
        report.Add("INFO", "C4 Same name twice in one folder",
            $"second create → HTTP {(int)st2}; files named 1.mrs now: {dupes.Count}. "
            + "Drive does not enforce unique names: create-only must come from elsewhere.");

        // Create with a pre-generated id, twice: the only server-side conflict Drive offers.
        var ids = await driveA.GenerateIdsAsync(1);
        var (st3, _) = await driveA.CreateFileAsync("2.mrs", ops, RandomNumberGenerator.GetBytes(16), ids.FirstOrDefault());
        var (st4, _) = await driveA.CreateFileAsync("2.mrs", ops, RandomNumberGenerator.GetBytes(16), ids.FirstOrDefault());
        report.Add(st3 == HttpStatusCode.OK && st4 == HttpStatusCode.Conflict ? "PASS" : "INFO", "C4b Create with a generated id, twice",
            $"first → HTTP {(int)st3}, second → HTTP {(int)st4} (409 expected: idempotent retry, not a name lock).");

        // Update content (device record).
        var (upd, _) = await driveA.UpdateContentAsync(f1!, RandomNumberGenerator.GetBytes(1024));
        report.Add(upd == HttpStatusCode.OK ? "PASS" : "FAIL", "C5 Replace content of an existing file", $"HTTP {(int)upd}.");

        // Resumable upload: is a partial upload listed?
        await ResumableAsync(driveA, report, ops, options.LargeMiB);

        // Delete twice.
        var (d1, _) = await driveA.DeleteAsync(f1!);
        var (d2, _) = await driveA.DeleteAsync(f1!);
        report.Add(d1 == HttpStatusCode.NoContent && d2 == HttpStatusCode.NotFound ? "PASS" : "INFO", "C7 Delete, then delete again",
            $"HTTP {(int)d1} then HTTP {(int)d2}.");

        // Changes feed.
        await ChangesAsync(driveA, report, ops);

        // Burst of small creates.
        var burstSw = Stopwatch.StartNew();
        var ok = 0;
        for (var i = 0; i < options.Burst; i++)
        {
            var (s, _) = await driveA.CreateFileAsync($"b{i}.mrs", ops, RandomNumberGenerator.GetBytes(4096));
            if (s == HttpStatusCode.OK) ok++;
        }
        report.Add(ok == options.Burst ? "PASS" : "FAIL", "C9 Burst of small creates",
            $"{ok}/{options.Burst}, {burstSw.ElapsedMilliseconds / Math.Max(1, options.Burst)} ms each; throttled: {driveA.Throttled}.");

        // Listing cost: one query for everything under the run (by parent is per folder).
        var listSw = Stopwatch.StartNew();
        var listed = await driveA.ListAsync($"'{ops}' in parents and trashed = false", "drive");
        report.Add("INFO", "C10 List one folder", $"{listed.Count} files in {listSw.ElapsedMilliseconds} ms.");

        // --- appDataFolder --------------------------------------------------
        var (ap, apId) = await driveA.CreateFileAsync($"{run}-app.mrs", "appDataFolder", RandomNumberGenerator.GetBytes(512));
        var appListed = await driveA.ListAsync($"name = '{run}-app.mrs'", "appDataFolder");
        report.Add(ap == HttpStatusCode.OK && appListed.Count == 1 ? "PASS" : "FAIL", "C11 appDataFolder create and list (client A)",
            $"create HTTP {(int)ap}, listed: {appListed.Count}.");

        // --- Client B: the cross-client question -----------------------------
        if (options.ClientBId is not null)
        {
            var b = new OAuthClient("B", options.ClientBId, options.ClientBSecret!, Path.Combine(workDir, "token-b.protected"), http, report);
            Console.WriteLine("Sign in with the SAME Google account for client B.");
            await b.SignInAsync();
            var driveB = new Drive(http, b, report);

            var seenFolder = await driveB.ListAsync($"name = '{run}' and trashed = false", "drive");
            report.Add(seenFolder.Count == 1 ? "PASS" : "FAIL", "C12 drive.file: client B finds the folder client A created",
                $"found {seenFolder.Count}.");

            var seenFiles = await driveB.ListAsync($"'{ops}' in parents and trashed = false", "drive");
            var readable = "not tried";
            if (seenFiles.Count > 0)
            {
                var (rs, _) = await driveB.ReadAsync(seenFiles[0]);
                readable = $"HTTP {(int)rs}";
            }
            else
            {
                var (rs, _) = await driveB.ReadAsync(listed.FirstOrDefault() ?? "none");
                readable = $"by id → HTTP {(int)rs}";
            }
            report.Add(seenFiles.Count == listed.Count ? "PASS" : "FAIL", "C13 drive.file: client B lists and reads client A's files",
                $"listed {seenFiles.Count} of {listed.Count}; read {readable}.");

            var (bw, bFile) = await driveB.CreateFileAsync("from-b.mrs", ops, RandomNumberGenerator.GetBytes(64));
            var aSeesB = bFile is not null && (await driveA.ListAsync($"'{ops}' in parents and name = 'from-b.mrs'", "drive")).Count == 1;
            report.Add(bw == HttpStatusCode.OK && aSeesB ? "PASS" : "FAIL", "C14 drive.file: client B writes into A's folder, A sees it",
                $"create HTTP {(int)bw}; visible to A: {aSeesB}.");

            var appB = await driveB.ListAsync($"name = '{run}-app.mrs'", "appDataFolder");
            report.Add(appB.Count == 1 ? "PASS" : "FAIL", "C15 appDataFolder: client B sees client A's file",
                $"found {appB.Count}.");

            // Round 2: one query for a whole group, by a property set at creation.
            var props = new JsonObject { ["mrgroup"] = run };
            await driveA.CreateFileAsync("tagged.mrs", ops, RandomNumberGenerator.GetBytes(32), properties: props);
            var byPropA = await driveA.ListAsync($"properties has {{ key='mrgroup' and value='{run}' }} and trashed = false", "drive");
            var byPropB = await driveB.ListAsync($"properties has {{ key='mrgroup' and value='{run}' }} and trashed = false", "drive");
            var byAppPropA = await driveA.ListAsync($"appProperties has {{ key='mrgroup' and value='{run}' }} and trashed = false", "drive");
            var byAppPropB = await driveB.ListAsync($"appProperties has {{ key='mrgroup' and value='{run}' }} and trashed = false", "drive");
            report.Add(byPropB.Count == 1 ? "PASS" : "FAIL", "C16 Query by public property across clients",
                $"properties: A finds {byPropA.Count}, B finds {byPropB.Count}; appProperties: A finds {byAppPropA.Count}, B finds {byAppPropB.Count}.");

            // Round 2: does client B's changes feed report client A's new file?
            var tokenB = await driveB.StartPageTokenAsync();
            if (tokenB is not null)
            {
                await driveA.CreateFileAsync("cross.mrs", ops, RandomNumberGenerator.GetBytes(32));
                var (seenB, afterB, _, _) = await PollChangesAsync(driveB, tokenB, "cross.mrs", TimeSpan.FromSeconds(90));
                report.Add(seenB ? "PASS" : "FAIL", "C17 Changes feed across clients (B sees A's new file)",
                    seenB ? $"after {afterB.TotalSeconds:F0} s." : "not within 90 s.");
            }

            // Round 2: list a whole tree with one query per folder level vs by property: timing.
            var swAll = Stopwatch.StartNew();
            var all = await driveB.ListAsync($"properties has {{ key='mrgroup' and value='{run}' }}", "drive");
            report.Add("INFO", "C18 Group listing by property", $"{all.Count} file(s) in {swAll.ElapsedMilliseconds} ms (one paged query).");
        }
        else
        {
            report.Add("INFO", "C12–C15 Cross-client visibility", "Skipped: pass --client-b-id and --client-b-secret.");
        }

        // --- Cleanup ------------------------------------------------------------
        if (!options.Keep)
        {
            var (dr, _) = await driveA.DeleteAsync(root);
            if (apId is not null) await driveA.DeleteAsync(apId);
            report.Add(dr == HttpStatusCode.NoContent ? "PASS" : "INFO", "C99 Cleanup", $"Deleted the run folder: HTTP {(int)dr}.");
        }
        else
        {
            report.Add("INFO", "C99 Cleanup", $"Kept `{run}` (--keep).");
        }
    }

    private static async Task ResumableAsync(Drive drive, Report report, string parent, int mib)
    {
        var content = RandomNumberGenerator.GetBytes(mib * 1024 * 1024);
        var (st, uploadUrl) = await drive.StartResumableAsync("big.mrc", parent, content.Length);
        if (uploadUrl is null)
        {
            report.Add("FAIL", "C6 Resumable upload", $"start → HTTP {(int)st}");
            return;
        }
        const int chunk = 256 * 1024 * 16; // multiples of 256 KiB
        var sw = Stopwatch.StartNew();
        HttpStatusCode last = 0;
        var visiblePartial = false;
        for (var offset = 0; offset < content.Length; offset += chunk)
        {
            var size = Math.Min(chunk, content.Length - offset);
            last = await drive.PutChunkAsync(uploadUrl, content, offset, size);
            if (offset == 0)
            {
                visiblePartial = (await drive.ListAsync($"'{parent}' in parents and name = 'big.mrc' and trashed = false", "drive")).Count > 0;
            }
        }
        report.Add(last is HttpStatusCode.OK or HttpStatusCode.Created ? "PASS" : "FAIL", "C6 Resumable upload",
            $"{mib} MiB in {sw.Elapsed.TotalSeconds:F1} s, last chunk HTTP {(int)last}.");
        report.Add(visiblePartial ? "FAIL" : "PASS", "C6b Partial resumable upload is invisible",
            $"listed after the first chunk: {visiblePartial}.");
    }

    private static async Task ChangesAsync(Drive drive, Report report, string parent)
    {
        var token = await drive.StartPageTokenAsync();
        if (token is null)
        {
            report.Add("FAIL", "C8 Changes feed", "getStartPageToken failed.");
            return;
        }
        await drive.CreateFileAsync("changed.mrs", parent, RandomNumberGenerator.GetBytes(64));
        var (seen, after, names, hasParents) = await PollChangesAsync(drive, token, "changed.mrs", TimeSpan.FromSeconds(90));
        report.Add(seen ? "PASS" : "FAIL", "C8 Changes feed (drive space), same client",
            $"new file reported after {after.TotalSeconds:F0} s (polled every 3 s, up to 90 s); "
            + $"names in the last poll: {string.Join(", ", names.Distinct().Take(5))}; parents included: {hasParents}.");
    }

    // Polls the feed from a fixed cursor until `name` appears.
    internal static async Task<(bool Seen, TimeSpan After, List<string> Names, bool HasParents)> PollChangesAsync(
        Drive drive, string token, string name, TimeSpan limit)
    {
        var sw = Stopwatch.StartNew();
        List<string> names = [];
        var hasParents = false;
        while (sw.Elapsed < limit)
        {
            (names, _, hasParents) = await drive.ChangesAsync(token, "drive");
            if (names.Contains(name)) return (true, sw.Elapsed, names, hasParents);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
        return (false, sw.Elapsed, names, hasParents);
    }

    internal static string Short(string text) => text.Length <= 200 ? text : text[..200] + "…";
}

// OAuth 2.0 for installed apps: system browser, loopback redirect on
// 127.0.0.1 with a random port (a TcpListener, so no URL ACL), PKCE S256,
// client secret sent as Google requires for Desktop clients (not treated
// as confidential by Google). Refresh token cached with DPAPI.
internal sealed class OAuthClient(string label, string clientId, string clientSecret, string cachePath, HttpClient http, Report report)
{
    private string? _accessToken;
    private DateTimeOffset _expires;
    private string? _refreshToken;

    public string Label => label;

    public async Task SignInAsync()
    {
        if (File.Exists(cachePath))
        {
            _refreshToken = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(cachePath), null, DataProtectionScope.CurrentUser));
            try
            {
                await RefreshAsync();
                report.Add("PASS", $"C1{label} Refresh from the cached refresh token", "No browser needed.");
                return;
            }
            catch (Exception ex)
            {
                report.Add("INFO", $"C1{label} Refresh from the cached refresh token", $"Failed ({Program.Short(ex.Message)}); signing in again.");
            }
        }

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var url = "https://accounts.google.com/o/oauth2/v2/auth"
            + $"?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}"
            + $"&response_type=code&scope={Uri.EscapeDataString(Program_Scopes)}"
            + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}"
            + "&access_type=offline&prompt=consent";
        Console.WriteLine($"Client {label}: opening the browser for sign-in…");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        var sw = Stopwatch.StartNew();
        string? code;
        using (var client = await listener.AcceptTcpClientAsync())
        using (var stream = client.GetStream())
        {
            var buffer = new byte[8192];
            var read = await stream.ReadAsync(buffer);
            var requestLine = Encoding.ASCII.GetString(buffer, 0, read).Split("\r\n")[0];
            var target = requestLine.Split(' ')[1];
            var query = System.Web.HttpUtility.ParseQueryString(new Uri("http://x" + target).Query);
            code = query["code"];
            var ok = code is not null && query["state"] == state;
            var body = ok ? "Sign-in complete. You can close this tab." : "Sign-in failed.";
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(response));
            if (!ok) throw new InvalidOperationException($"Authorization failed: {query["error"] ?? "state mismatch"}");
        }
        listener.Stop();

        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code!,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier,
        });
        _refreshToken = token["refresh_token"]?.GetValue<string>();
        var rtExpires = token["refresh_token_expires_in"]?.GetValue<long>();
        report.Add(_refreshToken is not null ? "PASS" : "FAIL", $"C0{label} Sign-in (loopback + PKCE)",
            $"{sw.Elapsed.TotalSeconds:F0} s; scopes: {token["scope"]?.GetValue<string>()?.Replace("https://www.googleapis.com/auth/", "", StringComparison.Ordinal)}; "
            + $"refresh token: {(_refreshToken is not null ? "yes" : "no")}; refresh_token_expires_in: "
            + (rtExpires is { } s ? $"{s} s (~{s / 86400.0:F1} days: consent screen in Testing?)" : "absent (no fixed expiry)"));
        if (_refreshToken is not null)
        {
            File.WriteAllBytes(cachePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(_refreshToken), null, DataProtectionScope.CurrentUser));
        }
    }

    public async Task<string> TokenAsync(bool force = false)
    {
        if (force || _accessToken is null || DateTimeOffset.UtcNow > _expires) await RefreshAsync();
        return _accessToken!;
    }

    private async Task RefreshAsync()
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = _refreshToken!,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
        });
        _accessToken = token["access_token"]!.GetValue<string>();
        _expires = DateTimeOffset.UtcNow.AddSeconds((token["expires_in"]?.GetValue<int>() ?? 3600) - 60);
    }

    private async Task<JsonNode> PostTokenAsync(Dictionary<string, string> form)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form));
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var error = JsonNode.Parse(text)?["error"]?.ToString() ?? "unknown";
            throw new InvalidOperationException($"Token endpoint HTTP {(int)response.StatusCode} ({error})");
        }
        _accessToken = JsonNode.Parse(text)!["access_token"]?.GetValue<string>() ?? _accessToken;
        _expires = DateTimeOffset.UtcNow.AddMinutes(55);
        return JsonNode.Parse(text)!;
    }

    private const string Program_Scopes = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/drive.appdata";

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class Drive(HttpClient http, OAuthClient auth, Report report)
{
    private const string Api = "https://www.googleapis.com/drive/v3";
    private const string Upload = "https://www.googleapis.com/upload/drive/v3";
    private const string FolderMime = "application/vnd.google-apps.folder";

    // Resumable chunks answer 308 "Resume Incomplete": not a redirect.
    private static readonly HttpClient NoRedirect = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public int Throttled { get; private set; }

    public async Task<string?> CreateFolderAsync(string name, string? parent)
    {
        var meta = new JsonObject { ["name"] = name, ["mimeType"] = FolderMime };
        if (parent is not null) meta["parents"] = new JsonArray(parent);
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, $"{Api}/files?fields=id")
        {
            Content = new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"),
        });
        return response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync())!["id"]!.GetValue<string>() : null;
    }

    public async Task<(HttpStatusCode, string?)> CreateFileAsync(string name, string parent, byte[] content, string? id = null,
        JsonObject? properties = null)
    {
        var meta = new JsonObject { ["name"] = name, ["parents"] = new JsonArray(parent) };
        if (id is not null) meta["id"] = id;
        if (properties is not null)
        {
            meta["properties"] = properties.DeepClone();
            meta["appProperties"] = properties.DeepClone();
        }
        using var response = await SendAsync(() =>
        {
            var multipart = new MultipartContent("related");
            multipart.Add(new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"));
            var bytes = new ByteArrayContent(content);
            bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(bytes);
            return new HttpRequestMessage(HttpMethod.Post, $"{Upload}/files?uploadType=multipart&fields=id") { Content = multipart };
        });
        var fileId = response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync())!["id"]!.GetValue<string>() : null;
        return (response.StatusCode, fileId);
    }

    public async Task<(HttpStatusCode, string?)> UpdateContentAsync(string id, byte[] content)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Patch, $"{Upload}/files/{id}?uploadType=media")
        {
            Content = new ByteArrayContent(content),
        });
        return (response.StatusCode, null);
    }

    public async Task<(HttpStatusCode, byte[])> ReadAsync(string id)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files/{id}?alt=media"));
        return (response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : []);
    }

    public async Task<(HttpStatusCode, string?)> DeleteAsync(string id)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{Api}/files/{id}"));
        return (response.StatusCode, null);
    }

    public async Task<List<string>> GenerateIdsAsync(int count)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/files/generateIds?count={count}&space=drive"));
        if (!response.IsSuccessStatusCode) return [];
        return [.. JsonNode.Parse(await response.Content.ReadAsStringAsync())!["ids"]!.AsArray().Select(i => i!.GetValue<string>())];
    }

    public async Task<List<string>> ListAsync(string query, string spaces)
    {
        var ids = new List<string>();
        string? pageToken = null;
        do
        {
            var url = $"{Api}/files?q={Uri.EscapeDataString(query)}&spaces={spaces}&pageSize=1000&fields=nextPageToken,files(id,name)"
                + (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
            if (!response.IsSuccessStatusCode)
            {
                report.Add("INFO", "List failed", $"HTTP {(int)response.StatusCode} for spaces={spaces}.");
                return ids;
            }
            var page = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            ids.AddRange(page["files"]!.AsArray().Select(f => f!["id"]!.GetValue<string>()));
            pageToken = page["nextPageToken"]?.GetValue<string>();
        }
        while (pageToken is not null);
        return ids;
    }

    public async Task<(HttpStatusCode, string?)> StartResumableAsync(string name, string parent, long length)
    {
        var meta = new JsonObject { ["name"] = name, ["parents"] = new JsonArray(parent) };
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{Upload}/files?uploadType=resumable&fields=id")
            {
                Content = new StringContent(meta.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Upload-Content-Length", length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return request;
        });
        return (response.StatusCode, response.Headers.Location?.ToString());
    }

    public async Task<HttpStatusCode> PutChunkAsync(string uploadUrl, byte[] content, int offset, int length)
    {
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(content, offset, length) };
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + length - 1, content.Length);
            return request;
        }, NoRedirect);
        // 308 Resume Incomplete between chunks.
        return response.StatusCode;
    }

    public async Task<string?> StartPageTokenAsync()
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{Api}/changes/startPageToken"));
        return response.IsSuccessStatusCode
            ? JsonNode.Parse(await response.Content.ReadAsStringAsync())!["startPageToken"]!.GetValue<string>()
            : null;
    }

    public async Task<(List<string> Names, string? NewToken, bool HasParents)> ChangesAsync(string token, string spaces)
    {
        var names = new List<string>();
        var hasParents = false;
        string? next = token;
        string? newStart = null;
        while (next is not null)
        {
            var url = $"{Api}/changes?pageToken={Uri.EscapeDataString(next)}&spaces={spaces}&fields=nextPageToken,newStartPageToken,changes(removed,file(name,parents,trashed))";
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
            if (!response.IsSuccessStatusCode) return (names, null, false);
            var page = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            foreach (var change in page["changes"]!.AsArray())
            {
                if (change?["file"]?["name"]?.GetValue<string>() is { } name) names.Add(name);
                hasParents |= change?["file"]?["parents"] is not null;
            }
            next = page["nextPageToken"]?.GetValue<string>();
            newStart = page["newStartPageToken"]?.GetValue<string>() ?? newStart;
        }
        return (names, newStart, hasParents);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, HttpClient? client = null)
    {
        var refreshed = false;
        for (var attempt = 0; ; attempt++)
        {
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.TokenAsync(refreshed));
            var response = await (client ?? http).SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                response.Dispose();
                refreshed = true;
                continue;
            }
            if ((response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                 || (response.StatusCode == HttpStatusCode.Forbidden && (await response.Content.ReadAsStringAsync()).Contains("rateLimitExceeded", StringComparison.Ordinal)))
                && attempt < 4)
            {
                Throttled++;
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                continue;
            }
            return response;
        }
    }
}

internal sealed class Report(Options options)
{
    private readonly List<(string Result, string Check, string Detail)> _rows = [];

    public bool Failed => _rows.Any(r => r.Result is "FAIL" or "ABORT");

    public void Add(string result, string check, string detail)
    {
        _rows.Add((result, check, detail));
        Console.WriteLine($"[{result}] {check}: {detail}");
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Spike S7 — Google Drive");
        sb.AppendLine();
        sb.AppendLine($"- Date (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"- OS: {Environment.OSVersion.VersionString}; .NET {Environment.Version}");
        sb.AppendLine($"- Client B: {(options.ClientBId is null ? "not used" : "used")}; large file: {options.LargeMiB} MiB; burst: {options.Burst}");
        sb.AppendLine();
        sb.AppendLine("| Result | Check | Detail |");
        sb.AppendLine("|---|---|---|");
        foreach (var (result, check, detail) in _rows)
            sb.AppendLine($"| {result} | {check} | {detail.Replace("|", "\\|", StringComparison.Ordinal)} |");
        return sb.ToString();
    }
}

internal sealed record Options(string ClientAId, string ClientASecret, string? ClientBId, string? ClientBSecret, bool Keep, int LargeMiB, int Burst)
{
    public const string Usage = """
        Usage: dotnet run -- --client-a-id <id> --client-a-secret <secret> [options]
          --client-b-id <id> --client-b-secret <secret>   second Desktop client of the same project (cross-client checks)
          --large-mib <n>   size of the resumable upload, default 6
          --burst <n>       number of small creates timed, default 20
          --keep            keep the test files in Drive
        """;

    public static Options? Parse(string[] args)
    {
        string? aId = null, aSecret = null, bId = null, bSecret = null;
        var keep = false;
        var large = 6;
        var burst = 20;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--client-a-id" when i + 1 < args.Length: aId = args[++i]; break;
                case "--client-a-secret" when i + 1 < args.Length: aSecret = args[++i]; break;
                case "--client-b-id" when i + 1 < args.Length: bId = args[++i]; break;
                case "--client-b-secret" when i + 1 < args.Length: bSecret = args[++i]; break;
                case "--large-mib" when i + 1 < args.Length: large = int.Parse(args[++i]); break;
                case "--burst" when i + 1 < args.Length: burst = int.Parse(args[++i]); break;
                case "--keep": keep = true; break;
                default: return null;
            }
        }
        if (aId is null || aSecret is null || (bId is null) != (bSecret is null)) return null;
        return new Options(aId, aSecret, bId, bSecret, keep, large, burst);
    }
}
