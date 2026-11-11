using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;

namespace ArqamBase.App.Core;

/// <summary>Google refused or could not be reached.</summary>
public sealed class GoogleException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Signing in to the school's Google account (tblGoogleOAuth: client ID and secret, refresh token) for its contacts.
/// The first sign-in opens the browser; after that the saved refresh token is used. The OAuth client must be of type
/// "Desktop app" in Google Cloud, so that the browser can hand the sign-in back to this computer (http://127.0.0.1).
/// </summary>
public static class GoogleAuth
{
    public const string Scope = "https://www.googleapis.com/auth/contacts";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static GoogleOAuth Load(ArqamDbContext db) => db.GoogleOAuth.OrderBy(o => o.Id).FirstOrDefault() ?? new GoogleOAuth { Id = 1 };

    public static void Save(ArqamDbContext db, GoogleOAuth row)
    {
        row.LastUpdated = DateTime.Now;
        if (db.Entry(row).State == EntityState.Detached) db.GoogleOAuth.Add(row);
        db.SaveChanges();
    }

    /// <summary>A valid access token: the saved one, else one from the refresh token. Null when Google needs a new sign-in.</summary>
    public static async Task<string?> AccessTokenAsync(AppConfig config, UserSession session, CancellationToken cancel)
    {
        using var db = config.CreateContext(session);
        var row = Load(db);
        if (row.AccessToken is { Length: > 0 } token && row.ExpiresAt > DateTime.Now.AddMinutes(2)) return token;
        if (string.IsNullOrWhiteSpace(row.RefreshToken) || string.IsNullOrWhiteSpace(row.ClientId)) return null;
        var reply = await TokenAsync(new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = row.RefreshToken!, ["client_id"] = row.ClientId!.Trim(), ["client_secret"] = row.ClientSecret?.Trim() ?? "",
        }, cancel, signInAgainOnRefusal: true);
        if (reply is null) return null;
        Keep(row, reply);
        Save(db, row);
        return row.AccessToken;
    }

    /// <summary>Opens the browser to sign in to Google, waits for it to come back, and saves the tokens.</summary>
    public static async Task SignInAsync(AppConfig config, UserSession session, CancellationToken cancel)
    {
        using var db = config.CreateContext(session);
        var row = Load(db);
        if (string.IsNullOrWhiteSpace(row.ClientId)) throw new GoogleException("Enter the Google client ID and secret first.");

        var port = FreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var url = "https://accounts.google.com/o/oauth2/v2/auth?response_type=code&access_type=offline&prompt=consent" +
                  $"&client_id={Uri.EscapeDataString(row.ClientId!.Trim())}&redirect_uri={Uri.EscapeDataString(redirect)}&scope={Uri.EscapeDataString(Scope)}" +
                  $"&code_challenge={challenge}&code_challenge_method=S256&state={state}" +
                  (string.IsNullOrWhiteSpace(row.GoogleAccount) ? "" : $"&login_hint={Uri.EscapeDataString(row.GoogleAccount!.Trim())}");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        HttpListenerContext ctx;
        try { ctx = await listener.GetContextAsync().WaitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw new GoogleException("The Google sign-in was not finished within 5 minutes."); }
        var query = System.Web.HttpUtility.ParseQueryString(ctx.Request.Url!.Query);
        var ok = query["state"] == state && query["code"] is { Length: > 0 };
        var page = Encoding.UTF8.GetBytes(ok
            ? "<html><body style='font-family:Segoe UI'><h2>Signed in.</h2><p>You can close this tab and go back to ArqamBase.</p></body></html>"
            : "<html><body style='font-family:Segoe UI'><h2>Sign-in did not finish.</h2><p>Go back to ArqamBase and try again.</p></body></html>");
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.OutputStream.WriteAsync(page, cancel);
        ctx.Response.Close();
        if (!ok) throw new GoogleException($"Google did not sign in: {query["error"] ?? "the answer did not match"}.");

        var reply = await TokenAsync(new()
        {
            ["grant_type"] = "authorization_code", ["code"] = query["code"]!, ["redirect_uri"] = redirect, ["code_verifier"] = verifier,
            ["client_id"] = row.ClientId!.Trim(), ["client_secret"] = row.ClientSecret?.Trim() ?? "",
        }, cancel, signInAgainOnRefusal: false);
        Keep(row, reply!);
        Save(db, row);
    }

    private static void Keep(GoogleOAuth row, JsonNode reply)
    {
        row.AccessToken = reply["access_token"]?.ToString();
        row.TokenType = reply["token_type"]?.ToString();
        row.Scope = reply["scope"]?.ToString() ?? row.Scope;
        row.ExpiresAt = DateTime.Now.AddSeconds(reply["expires_in"]?.GetValue<int>() ?? 3600);
        if (reply["refresh_token"]?.ToString() is { Length: > 0 } refresh) row.RefreshToken = refresh;
    }

    /// <summary>Calls Google's token endpoint. With <paramref name="signInAgainOnRefusal"/>, a refused refresh token gives null (sign in again).</summary>
    private static async Task<JsonNode?> TokenAsync(Dictionary<string, string> form, CancellationToken cancel, bool signInAgainOnRefusal)
    {
        try
        {
            using var response = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancel);
            var body = await response.Content.ReadAsStringAsync(cancel);
            if (response.IsSuccessStatusCode) return JsonNode.Parse(body);
            if (signInAgainOnRefusal && body.Contains("invalid_grant")) return null;
            throw new GoogleException($"Google refused the sign-in ({(int)response.StatusCode}): {body[..Math.Min(body.Length, 300)]}");
        }
        catch (HttpRequestException ex) { throw new GoogleException($"Could not reach Google: {ex.Message}", ex); }
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>A contact as read from Google: what the sync compares, plus what it must send back unchanged.</summary>
public sealed record GoogleContact(string ResourceName, string Etag, string Prefix, string Given, string Middle, string Family, string Suffix,
    JsonArray Phones, JsonArray UserDefined, IReadOnlyList<string> Groups)
{
    public string Custom(string key) =>
        UserDefined.OfType<JsonObject>().FirstOrDefault(u => string.Equals(u["key"]?.ToString()?.Trim(), key, StringComparison.OrdinalIgnoreCase))?["value"]?.ToString()?.Trim() ?? "";
}

/// <summary>A contact label (group) in Google Contacts.</summary>
public sealed record GoogleGroup(string ResourceName, string Name, int Members);

/// <summary>The Google People API calls the contact sync needs.</summary>
public sealed class GooglePeople(string accessToken)
{
    private const string Api = "https://people.googleapis.com/v1/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<List<GoogleContact>> ContactsAsync(CancellationToken cancel)
    {
        var list = new List<GoogleContact>();
        string? page = null;
        do
        {
            var url = "people/me/connections?pageSize=1000&sources=READ_SOURCE_TYPE_CONTACT&personFields=names,phoneNumbers,userDefined,memberships,metadata" +
                      (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
            var reply = await SendAsync(HttpMethod.Get, url, null, cancel);
            foreach (var p in reply["connections"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                var name = p["names"]?.AsArray().OfType<JsonObject>().FirstOrDefault();
                string Part(string key) => name?[key]?.ToString()?.Trim() ?? "";
                var groups = p["memberships"]?.AsArray().OfType<JsonObject>()
                    .Select(m => m["contactGroupMembership"]?["contactGroupResourceName"]?.ToString()).OfType<string>().ToList() ?? [];
                list.Add(new GoogleContact(p["resourceName"]!.ToString(), p["etag"]?.ToString() ?? "",
                    Part("honorificPrefix"), Part("givenName"), Part("middleName"), Part("familyName"), Part("honorificSuffix"),
                    Clean(p["phoneNumbers"], "value", "type"), Clean(p["userDefined"], "key", "value"), groups));
            }
            page = reply["nextPageToken"]?.ToString();
        } while (!string.IsNullOrEmpty(page));
        return list;
    }

    /// <summary>Only the fields that can be written back (Google rejects some read-only ones).</summary>
    private static JsonArray Clean(JsonNode? items, params string[] keep) =>
        new(items?.AsArray().OfType<JsonObject>().Select(o =>
        {
            var copy = new JsonObject();
            foreach (var k in keep) if (o[k] is { } v) copy[k] = v.DeepClone();
            return (JsonNode)copy;
        }).ToArray() ?? []);

    public async Task<List<GoogleGroup>> GroupsAsync(CancellationToken cancel)
    {
        var reply = await SendAsync(HttpMethod.Get, "contactGroups?pageSize=1000", null, cancel);
        return reply["contactGroups"]?.AsArray().OfType<JsonObject>()
            .Select(g => new GoogleGroup(g["resourceName"]!.ToString(), g["formattedName"]?.ToString() ?? g["name"]?.ToString() ?? "", g["memberCount"]?.GetValue<int>() ?? 0))
            .ToList() ?? [];
    }

    public async Task<GoogleGroup> CreateGroupAsync(string name, CancellationToken cancel)
    {
        var reply = await SendAsync(HttpMethod.Post, "contactGroups", new JsonObject { ["contactGroup"] = new JsonObject { ["name"] = name } }, cancel);
        return new GoogleGroup(reply["resourceName"]!.ToString(), name, 0);
    }

    /// <summary>Creates up to 200 contacts in one call.</summary>
    public Task CreateAsync(IEnumerable<JsonObject> people, CancellationToken cancel) =>
        SendAsync(HttpMethod.Post, "people:batchCreateContacts", new JsonObject
        {
            ["contacts"] = new JsonArray(people.Select(p => (JsonNode)new JsonObject { ["contactPerson"] = p }).ToArray()),
            ["readMask"] = "names", ["sources"] = new JsonArray("READ_SOURCE_TYPE_CONTACT"),
        }, cancel);

    /// <summary>Updates up to 200 contacts' names, phone numbers and custom fields in one call (each person carries its etag).</summary>
    public Task UpdateAsync(IEnumerable<(string ResourceName, JsonObject Person)> people, CancellationToken cancel)
    {
        var contacts = new JsonObject();
        foreach (var (resource, person) in people) contacts[resource] = person;
        return SendAsync(HttpMethod.Post, "people:batchUpdateContacts", new JsonObject
        {
            ["contacts"] = contacts, ["updateMask"] = "names,phoneNumbers,userDefined",
            ["readMask"] = "names", ["sources"] = new JsonArray("READ_SOURCE_TYPE_CONTACT"),
        }, cancel);
    }

    /// <summary>Adds contacts to a label and/or takes them off it (up to 1000).</summary>
    public Task ModifyGroupAsync(string group, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancel) =>
        SendAsync(HttpMethod.Post, $"{group}/members:modify", new JsonObject
        {
            ["resourceNamesToAdd"] = new JsonArray(add.Select(a => (JsonNode)a).ToArray()),
            ["resourceNamesToRemove"] = new JsonArray(remove.Select(r => (JsonNode)r).ToArray()),
        }, cancel);

    /// <summary>One call; when Google says too many requests, waits and tries again (twice).</summary>
    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancel)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, Api + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (body is not null) request.Content = JsonContent.Create(body);
            string text;
            HttpStatusCode status;
            try
            {
                using var response = await Http.SendAsync(request, cancel);
                text = await response.Content.ReadAsStringAsync(cancel);
                status = response.StatusCode;
            }
            catch (HttpRequestException ex) { throw new GoogleException($"Could not reach Google: {ex.Message}", ex); }
            if ((int)status is >= 200 and < 300) return text.Length == 0 ? new JsonObject() : JsonNode.Parse(text) ?? new JsonObject();
            if ((status == HttpStatusCode.TooManyRequests || (int)status >= 500) && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(30 * (attempt + 1)), cancel);
                continue;
            }
            if (status == HttpStatusCode.Unauthorized) throw new GoogleException("Google says the sign-in has expired. Press Sign in to Google.");
            throw new GoogleException($"Google answered {(int)status}: {text[..Math.Min(text.Length, 400)]}");
        }
    }
}
