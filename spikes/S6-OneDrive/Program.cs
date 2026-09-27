// Spike S6 (docs/analysis/ANALYSIS-B1-MOBILE-SYNC.md §13 Phase 0, §5.8):
// what the Microsoft Graph app folder offers the sync transport of
// Phase 4a. Throw-away code: it is not part of MedReminder.sln and its
// results go to §18.6 of the analysis.
//
// It writes only under the app folder (/Apps/<app name>/s6-<run>/) and,
// for its token cache and report, under %LOCALAPPDATA%\MedReminder\spike-s6\.
// It never prints tokens, the account name or the drive id.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Identity.Client;

namespace S6.OneDrive;

internal static class Program
{
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private const string PersonalTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";
    private static readonly string[] Scopes = ["Files.ReadWrite.AppFolder", "offline_access"];

    private static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null)
        {
            Console.WriteLine(Options.Usage);
            return 2;
        }

        var workDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MedReminder", "spike-s6");
        Directory.CreateDirectory(workDir);
        var report = new Report(options);

        try
        {
            await RunAsync(options, workDir, report);
        }
        catch (Exception ex)
        {
            report.Add("ABORT", "Unhandled", $"{ex.GetType().Name}: {ex.Message}");
        }

        var path = Path.Combine(workDir, $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.md");
        await File.WriteAllTextAsync(path, report.ToMarkdown());
        Console.WriteLine();
        Console.WriteLine($"Report written to {path}");
        return report.Failed ? 1 : 0;
    }

    private static async Task RunAsync(Options options, string workDir, Report report)
    {
        // --- Sign-in and token cache --------------------------------------
        var cachePath = Path.Combine(workDir, "msal.cache.protected");
        var cacheExisted = File.Exists(cachePath);
        var app = BuildApp(options, cachePath);

        AuthenticationResult auth;
        var accounts = (await app.GetAccountsAsync()).ToList();
        if (accounts.Count > 0)
        {
            try
            {
                auth = await app.AcquireTokenSilent(Scopes, accounts[0]).ExecuteAsync();
                report.Add("PASS", "C1 Silent sign-in from the persisted cache",
                    "A previous run's DPAPI-protected cache gave a token without a browser.");
            }
            catch (MsalUiRequiredException ex)
            {
                report.Add("INFO", "C1 Silent sign-in from the persisted cache", $"UI required: {ex.ErrorCode}");
                auth = await Interactive(app);
            }
        }
        else
        {
            report.Add("INFO", "C1 Silent sign-in from the persisted cache",
                cacheExisted ? "Cache present but without an account." : "First run: no cache yet. Run the tool a second time to test this.");
            var sw = Stopwatch.StartNew();
            auth = await Interactive(app);
            report.Add("PASS", "C0 Interactive sign-in (system browser, http://localhost)",
                $"Completed in {sw.Elapsed.TotalSeconds:F0} s.");
        }

        var accountKind = auth.TenantId == PersonalTenant ? "personal Microsoft account" : "work or school account";
        report.Add("INFO", "C0 Account type", $"{accountKind}; granted scopes: {string.Join(' ', auth.Scopes.Select(ShortScope))}");

        // A second application instance reading the same file proves the
        // cache survives a restart of the app.
        var app2 = BuildApp(options, cachePath);
        var accounts2 = (await app2.GetAccountsAsync()).ToList();
        try
        {
            await app2.AcquireTokenSilent(Scopes, accounts2.First()).WithForceRefresh(true).ExecuteAsync();
            report.Add("PASS", "C2 Refresh token from a fresh app instance", "Forced refresh through the persisted cache succeeded.");
        }
        catch (Exception ex)
        {
            report.Add("FAIL", "C2 Refresh token from a fresh app instance", $"{ex.GetType().Name}: {Short(ex.Message)}");
        }

        using var http = new GraphClient(app, accounts2.FirstOrDefault() ?? (await app.GetAccountsAsync()).First(), report);

        // --- App folder -----------------------------------------------------
        var (rootStatus, root) = await http.GetJsonAsync($"{Graph}/me/drive/special/approot");
        if (rootStatus != HttpStatusCode.OK || root is null)
        {
            report.Add("FAIL", "C3 App folder (special/approot)", $"HTTP {(int)rootStatus}");
            return;
        }
        var appFolderName = root["name"]?.GetValue<string>() ?? "?";
        var appFolderParent = root["parentReference"]?["path"]?.GetValue<string>() ?? "?";
        var driveType = root["parentReference"]?["driveType"]?.GetValue<string>() ?? "?";
        report.Add("PASS", "C3 App folder (special/approot)",
            $"name `{appFolderName}`, parent `{appFolderParent}`, driveType `{driveType}`.");

        var (outsideStatus, _) = await http.GetJsonAsync($"{Graph}/me/drive/root/children?$top=1");
        report.Add(outsideStatus is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound ? "PASS" : "INFO",
            "C4 Scope isolation: drive root is not readable",
            $"GET /me/drive/root/children → HTTP {(int)outsideStatus}.");

        var run = $"s6-{DateTime.UtcNow:yyyyMMddHHmmss}";
        string Item(string relative) => $"{Graph}/me/drive/special/approot:/{run}/{relative}:";

        // --- Create-only small files ---------------------------------------
        var small = RandomNumberGenerator.GetBytes(2048);
        var s1 = await http.PutContentAsync($"{Item("ops/1/aaaa/1.mrs")}/content?@microsoft.graph.conflictBehavior=fail", small);
        report.Add(s1 == HttpStatusCode.Created ? "PASS" : "FAIL", "C5 Create-only write with missing parent folders",
            $"PUT …/{run}/ops/1/aaaa/1.mrs, conflictBehavior=fail → HTTP {(int)s1} (expected 201; parents created implicitly).");

        var s2 = await http.PutContentAsync($"{Item("ops/1/aaaa/1.mrs")}/content?@microsoft.graph.conflictBehavior=fail", small);
        report.Add(s2 == HttpStatusCode.Conflict ? "PASS" : "FAIL", "C6 Create-only write refuses an existing name",
            $"Second PUT → HTTP {(int)s2} (expected 409).");

        var s3 = await http.PutContentAsync($"{Item("devices/aaaa.mrd")}/content?@microsoft.graph.conflictBehavior=replace", small);
        var s4 = await http.PutContentAsync($"{Item("devices/aaaa.mrd")}/content?@microsoft.graph.conflictBehavior=replace", RandomNumberGenerator.GetBytes(1024));
        report.Add(s3 == HttpStatusCode.Created && s4 == HttpStatusCode.OK ? "PASS" : "FAIL", "C7 Replace write (device record)",
            $"First PUT → HTTP {(int)s3}, second → HTTP {(int)s4} (expected 201 then 200).");

        // --- Read ------------------------------------------------------------
        var (readStatus, readBytes) = await http.GetBytesAsync($"{Item("ops/1/aaaa/1.mrs")}/content");
        report.Add(readStatus == HttpStatusCode.OK && readBytes is not null && readBytes.AsSpan().SequenceEqual(small) ? "PASS" : "FAIL",
            "C8 Read back", $"HTTP {(int)readStatus}, bytes equal: {readBytes is not null && readBytes.AsSpan().SequenceEqual(small)}.");

        var (missingStatus, _) = await http.GetBytesAsync($"{Item("ops/1/aaaa/999.mrs")}/content");
        report.Add(missingStatus == HttpStatusCode.NotFound ? "PASS" : "FAIL", "C9 Read of a missing file",
            $"HTTP {(int)missingStatus} (expected 404).");

        // --- Large upload session -----------------------------------------
        await LargeUploadAsync(http, report, Item("checkpoints/1/aaaa-1.mrc"), Item("checkpoints/1"), options.LargeMiB);

        // --- Listing and delta ---------------------------------------------
        await ListingAsync(http, report, run);

        // --- Throughput ------------------------------------------------------
        var sw2 = Stopwatch.StartNew();
        var created = 0;
        for (var i = 2; i < 2 + options.BurstCount; i++)
        {
            var st = await http.PutContentAsync(
                $"{Item($"ops/1/aaaa/{i}.mrs")}/content?@microsoft.graph.conflictBehavior=fail", RandomNumberGenerator.GetBytes(4096));
            if (st == HttpStatusCode.Created) created++;
        }
        report.Add(created == options.BurstCount ? "PASS" : "FAIL", "C12 Burst of small creates",
            $"{created}/{options.BurstCount} created, {sw2.ElapsedMilliseconds / Math.Max(1, options.BurstCount)} ms each on average; throttled responses: {http.Throttled}.");

        // --- Delete ------------------------------------------------------------
        var d1 = await http.DeleteAsync(Item("ops/1/aaaa/2.mrs"));
        var d2 = await http.DeleteAsync(Item("ops/1/aaaa/2.mrs"));
        report.Add(d1 == HttpStatusCode.NoContent && d2 == HttpStatusCode.NotFound ? "PASS" : "FAIL", "C13 Delete, then delete again",
            $"HTTP {(int)d1} then HTTP {(int)d2} (expected 204 then 404; the transport maps 404 to a no-op).");

        // --- Windows OneDrive client --------------------------------------
        await LocalClientAsync(http, report, options, appFolderName, run, Item);

        // --- Cleanup -----------------------------------------------------------
        if (!options.Keep)
        {
            var d = await http.DeleteAsync($"{Graph}/me/drive/special/approot:/{run}:");
            report.Add(d == HttpStatusCode.NoContent ? "PASS" : "INFO", "C99 Cleanup", $"Deleted `{run}`: HTTP {(int)d}.");
        }
        else
        {
            report.Add("INFO", "C99 Cleanup", $"Kept `{run}` (--keep).");
        }
    }

    private static async Task LargeUploadAsync(GraphClient http, Report report, string item, string folder, int mib)
    {
        const int chunk = 320 * 1024 * 10; // Graph requires multiples of 320 KiB.
        var content = RandomNumberGenerator.GetBytes(mib * 1024 * 1024);
        var body = new JsonObject { ["item"] = new JsonObject { ["@microsoft.graph.conflictBehavior"] = "fail" } };

        var (st, session) = await http.PostJsonAsync($"{item}/createUploadSession", body);
        var uploadUrl = session?["uploadUrl"]?.GetValue<string>();
        if (st != HttpStatusCode.OK || uploadUrl is null)
        {
            report.Add("FAIL", "C10 Large upload session", $"createUploadSession → HTTP {(int)st}");
            return;
        }

        var sw = Stopwatch.StartNew();
        HttpStatusCode last = 0;
        var visibleWhilePartial = false;
        for (var offset = 0; offset < content.Length; offset += chunk)
        {
            var length = Math.Min(chunk, content.Length - offset);
            last = await http.PutChunkAsync(uploadUrl, content, offset, length);
            if (offset == 0)
            {
                // After the first chunk, before the last: the file must not
                // be visible yet (a reader would see a partial checkpoint).
                var (_, children) = await http.GetJsonAsync($"{folder}/children");
                visibleWhilePartial = children?["value"]?.AsArray().Any(c => c?["name"]?.GetValue<string>() == "aaaa-1.mrc") ?? false;
            }
        }
        report.Add(last is HttpStatusCode.Created or HttpStatusCode.OK ? "PASS" : "FAIL", "C10 Large upload session",
            $"{mib} MiB in {sw.Elapsed.TotalSeconds:F1} s, final chunk → HTTP {(int)last}.");
        report.Add(visibleWhilePartial ? "FAIL" : "PASS", "C10b Partial upload is invisible",
            $"Listed after the first chunk: visible = {visibleWhilePartial}.");

        // Create-only through an upload session: where does the conflict show?
        var (st2, session2) = await http.PostJsonAsync($"{item}/createUploadSession", body);
        if (st2 == HttpStatusCode.Conflict)
        {
            report.Add("PASS", "C11 Create-only upload session on an existing name", "Refused at createUploadSession (HTTP 409).");
            return;
        }
        var url2 = session2?["uploadUrl"]?.GetValue<string>();
        if (url2 is null)
        {
            report.Add("FAIL", "C11 Create-only upload session on an existing name", $"createUploadSession → HTTP {(int)st2}");
            return;
        }
        HttpStatusCode last2 = 0;
        for (var offset = 0; offset < content.Length; offset += chunk)
            last2 = await http.PutChunkAsync(url2, content, offset, Math.Min(chunk, content.Length - offset));
        report.Add(last2 == HttpStatusCode.Conflict ? "PASS" : "FAIL", "C11 Create-only upload session on an existing name",
            $"Session accepted (HTTP {(int)st2}); final chunk → HTTP {(int)last2} (expected 409 at commit).");
    }

    private static async Task ListingAsync(GraphClient http, Report report, string run)
    {
        // Recursive listing of a prefix: one children call per folder.
        var sw = Stopwatch.StartNew();
        var files = new List<string>();
        var calls = 0;
        var pending = new Queue<string>();
        pending.Enqueue(run);
        while (pending.Count > 0)
        {
            var folder = pending.Dequeue();
            string? url = $"{Graph}/me/drive/special/approot:/{folder}:/children?$select=name,folder,file,size&$top=200";
            while (url is not null)
            {
                calls++;
                var (st, page) = await http.GetJsonAsync(url);
                if (st != HttpStatusCode.OK || page is null) break;
                foreach (var child in page["value"]!.AsArray())
                {
                    var name = child!["name"]!.GetValue<string>();
                    if (child["folder"] is not null) pending.Enqueue($"{folder}/{name}");
                    else files.Add($"{folder}/{name}");
                }
                url = page["@odata.nextLink"]?.GetValue<string>();
            }
        }
        report.Add(files.Count >= 3 ? "PASS" : "FAIL", "C14 Recursive listing by folder",
            $"{files.Count} files, {calls} calls, {sw.ElapsedMilliseconds} ms.");

        // Delta on the app folder: a change cursor for §5.8.
        var (d1, first) = await http.GetJsonAsync($"{Graph}/me/drive/special/approot/delta");
        if (d1 != HttpStatusCode.OK || first is null)
        {
            report.Add("INFO", "C15 Delta on the app folder", $"HTTP {(int)d1}: no change cursor, listing only.");
            return;
        }
        var deltaLink = first["@odata.deltaLink"]?.GetValue<string>();
        var guard = 0;
        while (deltaLink is null && first?["@odata.nextLink"] is not null && guard++ < 50)
        {
            (_, first) = await http.GetJsonAsync(first["@odata.nextLink"]!.GetValue<string>());
            deltaLink = first?["@odata.deltaLink"]?.GetValue<string>();
        }
        if (deltaLink is null)
        {
            report.Add("INFO", "C15 Delta on the app folder", "No deltaLink returned.");
            return;
        }

        await http.PutContentAsync(
            $"{Graph}/me/drive/special/approot:/{run}/ops/1/bbbb/1.mrs:/content?@microsoft.graph.conflictBehavior=fail",
            RandomNumberGenerator.GetBytes(512));
        var (d2, second) = await http.GetJsonAsync(deltaLink);
        var names = second?["value"]?.AsArray().Select(v => v?["name"]?.GetValue<string>()).Where(n => n is not null).ToList() ?? [];
        var hasNew = names.Contains("1.mrs");
        report.Add(d2 == HttpStatusCode.OK && hasNew ? "PASS" : "INFO", "C15 Delta on the app folder",
            $"Initial delta OK; after one create the delta returned {names.Count} item(s), new file present: {hasNew}. "
            + "Items carry names and parent ids, not paths: a transport maps them through the parent chain.");
    }

    private static async Task LocalClientAsync(
        GraphClient http, Report report, Options options, string appFolderName, string run, Func<string, string> item)
    {
        var roots = new[] { "OneDriveConsumer", "OneDriveCommercial", "OneDrive" }
            .Select(Environment.GetEnvironmentVariable)
            .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();
        if (roots.Count == 0)
        {
            report.Add("INFO", "C16 Windows OneDrive client syncs the app folder", "No OneDrive client folder found (%OneDrive% not set).");
            return;
        }
        if (options.WaitMinutes == 0)
        {
            report.Add("INFO", "C16 Windows OneDrive client syncs the app folder", "Skipped (--wait-minutes 0).");
            return;
        }

        Console.WriteLine($"Waiting up to {options.WaitMinutes} min for the OneDrive client to download `{run}`…");
        var sw = Stopwatch.StartNew();
        string? localRun = null;
        while (sw.Elapsed < TimeSpan.FromMinutes(options.WaitMinutes) && localRun is null)
        {
            // The Apps folder name is localized in Explorer ("App" in
            // Italian); search two levels below each root for the run folder.
            localRun = roots
                .SelectMany(r => SafeDirectories(r).SelectMany(SafeDirectories))
                .Select(d => Path.Combine(d, run))
                .FirstOrDefault(Directory.Exists);
            if (localRun is null) await Task.Delay(TimeSpan.FromSeconds(10));
        }
        if (localRun is null)
        {
            report.Add("FAIL", "C16 Windows OneDrive client syncs the app folder",
                $"`{run}` did not appear under the OneDrive folder within {options.WaitMinutes} min.");
            return;
        }

        var localFile = Path.Combine(localRun, "ops", "1", "aaaa", "1.mrs");
        var fileSw = Stopwatch.StartNew();
        while (!File.Exists(localFile) && fileSw.Elapsed < TimeSpan.FromMinutes(2)) await Task.Delay(TimeSpan.FromSeconds(5));
        var relative = Path.GetRelativePath(roots.First(r => localRun.StartsWith(r, StringComparison.OrdinalIgnoreCase)), localRun);
        report.Add(File.Exists(localFile) ? "PASS" : "FAIL", "C16 Windows OneDrive client syncs the app folder",
            $"Run folder appeared after {sw.Elapsed.TotalSeconds:F0} s at `<OneDrive>\\{relative}` (Graph name `{appFolderName}`); "
            + $"a segment file present: {File.Exists(localFile)}. Files-on-demand may keep content online-only.");

        if (!options.WriteLocal)
        {
            report.Add("INFO", "C17 File written by the OneDrive client is visible through Graph", "Skipped (pass --write-local).");
            return;
        }
        var probe = Path.Combine(localRun, "local-probe.bin");
        await File.WriteAllBytesAsync(probe, RandomNumberGenerator.GetBytes(256));
        var probeSw = Stopwatch.StartNew();
        var seen = false;
        while (!seen && probeSw.Elapsed < TimeSpan.FromMinutes(options.WaitMinutes))
        {
            var (st, _) = await http.GetJsonAsync(item("local-probe.bin"));
            seen = st == HttpStatusCode.OK;
            if (!seen) await Task.Delay(TimeSpan.FromSeconds(10));
        }
        report.Add(seen ? "PASS" : "FAIL", "C17 File written by the OneDrive client is visible through Graph",
            seen ? $"Visible after {probeSw.Elapsed.TotalSeconds:F0} s." : $"Not visible within {options.WaitMinutes} min.");
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static IPublicClientApplication BuildApp(Options options, string cachePath)
    {
        var app = PublicClientApplicationBuilder.Create(options.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{options.Tenant}")
            .WithRedirectUri("http://localhost")
            .Build();

        // DPAPI-protected cache file: the shape the 4a token store will take.
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (!File.Exists(cachePath)) return;
            var data = ProtectedData.Unprotect(File.ReadAllBytes(cachePath), null, DataProtectionScope.CurrentUser);
            args.TokenCache.DeserializeMsalV3(data);
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            var data = ProtectedData.Protect(args.TokenCache.SerializeMsalV3(), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(cachePath, data);
        });
        return app;
    }

    private static Task<AuthenticationResult> Interactive(IPublicClientApplication app) =>
        app.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync();

    private static string ShortScope(string scope) => scope.Split('/').Last();

    internal static string Short(string text) => text.Length <= 160 ? text : text[..160] + "…";
}

internal sealed class GraphClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly HttpClient _upload = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly IPublicClientApplication _app;
    private readonly IAccount _account;
    private readonly Report _report;

    public GraphClient(IPublicClientApplication app, IAccount account, Report report)
    {
        _app = app;
        _account = account;
        _report = report;
    }

    public int Throttled { get; private set; }

    public async Task<(HttpStatusCode, JsonNode?)> GetJsonAsync(string url)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        return (response.StatusCode, response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync()) : null);
    }

    public async Task<(HttpStatusCode, byte[]?)> GetBytesAsync(string url)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
        return (response.StatusCode, response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null);
    }

    public async Task<HttpStatusCode> PutContentAsync(string url, byte[] content)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } },
        });
        return response.StatusCode;
    }

    public async Task<(HttpStatusCode, JsonNode?)> PostJsonAsync(string url, JsonNode body)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        });
        return (response.StatusCode, response.IsSuccessStatusCode ? JsonNode.Parse(await response.Content.ReadAsStringAsync()) : null);
    }

    public async Task<HttpStatusCode> DeleteAsync(string url)
    {
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Delete, url));
        return response.StatusCode;
    }

    // Upload session URLs are pre-authenticated: no Authorization header.
    public async Task<HttpStatusCode> PutChunkAsync(string uploadUrl, byte[] content, int offset, int length)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(content, offset, length),
        };
        request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + length - 1, content.Length);
        using var response = await _upload.SendAsync(request);
        return response.StatusCode;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await _app.AcquireTokenSilent(new[] { "Files.ReadWrite.AppFolder" }, _account).ExecuteAsync();
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var response = await _http.SendAsync(request);
            if ((response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) && attempt < 3)
            {
                Throttled++;
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _report.Add("INFO", "Throttling", $"HTTP {(int)response.StatusCode}, Retry-After {wait.TotalSeconds:F0} s.");
                response.Dispose();
                await Task.Delay(wait);
                continue;
            }
            return response;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _upload.Dispose();
    }
}

internal sealed class Report
{
    private readonly List<(string Result, string Check, string Detail)> _rows = [];
    private readonly Options _options;

    public Report(Options options) => _options = options;

    public bool Failed => _rows.Any(r => r.Result is "FAIL" or "ABORT");

    public void Add(string result, string check, string detail)
    {
        _rows.Add((result, check, detail));
        Console.WriteLine($"[{result}] {check}: {detail}");
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Spike S6 — OneDrive app folder");
        sb.AppendLine();
        sb.AppendLine($"- Date (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"- OS: {Environment.OSVersion.VersionString}; .NET {Environment.Version}");
        sb.AppendLine($"- MSAL: {typeof(PublicClientApplication).Assembly.GetName().Version}");
        sb.AppendLine($"- Authority: `{_options.Tenant}`; large file: {_options.LargeMiB} MiB; burst: {_options.BurstCount}");
        sb.AppendLine();
        sb.AppendLine("| Result | Check | Detail |");
        sb.AppendLine("|---|---|---|");
        foreach (var (result, check, detail) in _rows)
            sb.AppendLine($"| {result} | {check} | {detail.Replace("|", "\\|", StringComparison.Ordinal)} |");
        return sb.ToString();
    }
}

internal sealed record Options(string ClientId, string Tenant, int WaitMinutes, bool WriteLocal, bool Keep, int LargeMiB, int BurstCount)
{
    public const string Usage = """
        Usage: dotnet run -- --client-id <guid> [options]
          --tenant <consumers|common|organizations|tenant-id>   default: consumers
          --wait-minutes <n>   wait for the Windows OneDrive client (0 = skip), default 5
          --write-local        also write a probe file into the local OneDrive folder (C17)
          --large-mib <n>      size of the upload-session test file, default 6
          --burst <n>          number of small creates timed, default 20
          --keep               keep the test folder in the app folder
        """;

    public static Options? Parse(string[] args)
    {
        string? clientId = null;
        var tenant = "consumers";
        var wait = 5;
        var writeLocal = false;
        var keep = false;
        var large = 6;
        var burst = 20;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--client-id" when i + 1 < args.Length: clientId = args[++i]; break;
                case "--tenant" when i + 1 < args.Length: tenant = args[++i]; break;
                case "--wait-minutes" when i + 1 < args.Length: wait = int.Parse(args[++i]); break;
                case "--large-mib" when i + 1 < args.Length: large = int.Parse(args[++i]); break;
                case "--burst" when i + 1 < args.Length: burst = int.Parse(args[++i]); break;
                case "--write-local": writeLocal = true; break;
                case "--keep": keep = true; break;
                default: return null;
            }
        }
        return Guid.TryParse(clientId, out _) ? new Options(clientId!, tenant, wait, writeLocal, keep, large, burst) : null;
    }
}
