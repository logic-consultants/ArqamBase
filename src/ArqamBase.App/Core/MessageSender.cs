using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ArqamBase.App.Core;

/// <summary>
/// WhatsApp and email account details, kept encrypted for this Windows user on this computer, never in the database or the code.
/// </summary>
public sealed class MessageSettings
{
    public const string WppConnect = "Own gateway (WPPConnect)", GreenApi = "GreenAPI", WaApi = "waapi.app", Microsoft365 = "Microsoft 365 app registration", Smtp = "SMTP mail server";

    public string WhatsAppProvider { get; set; } = WppConnect;

    /// <summary>The own gateway's address, for example http://localhost:21465. Only used by the own gateway.</summary>
    public string? ServerUrl { get; set; } = "http://localhost:21465";
    public string? Instance { get; set; }
    public string? ApiKey { get; set; }

    public string EmailSignIn { get; set; } = Microsoft365;
    public string? SendFrom { get; set; }
    public string? ReplyTo { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? SmtpServer { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }

    /// <summary>Everything goes to the test number and address instead. Always on except on the live database.</summary>
    public bool TestMode { get; set; } = true;
    public string? TestNumber { get; set; }
    public string? TestEmail { get; set; }

    public bool WhatsAppReady => !string.IsNullOrWhiteSpace(Instance) && !string.IsNullOrWhiteSpace(ApiKey)
                                 && (WhatsAppProvider != WppConnect || Uri.TryCreate(ServerUrl?.Trim(), UriKind.Absolute, out _));

    public bool EmailReady => !string.IsNullOrWhiteSpace(SendFrom) && (EmailSignIn == Smtp
        ? !string.IsNullOrWhiteSpace(SmtpServer)
        : !string.IsNullOrWhiteSpace(TenantId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret));

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ArqamBase.Messages");

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArqamBase", "messaging.bin");

    public static MessageSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new MessageSettings();
            return JsonSerializer.Deserialize<MessageSettings>(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser)) ?? new MessageSettings();
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or UnauthorizedAccessException)
        {
            return new MessageSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(this), Entropy, DataProtectionScope.CurrentUser));
    }
}

/// <summary>A file sent with a message.</summary>
public sealed record MessageFile(string Name, byte[] Content);

/// <summary>A message the provider refused, or could not be reached for.</summary>
public sealed class MessageSendException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Sends one message; returns the provider's id for it, if it gives one.</summary>
public interface IMessageSender
{
    Task<string?> WhatsAppAsync(string number, string text, MessageFile? file, CancellationToken cancel);
    Task<string?> EmailAsync(string to, string subject, string text, MessageFile? file, CancellationToken cancel);
}

/// <summary>Sends through the WhatsApp provider and email account in the settings.</summary>
public sealed class MessageSender(MessageSettings settings) : IMessageSender
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private string GreenUrl(string kind, string method)
    {
        // The account's host starts with the first four digits of the instance id.
        var id = settings.Instance!.Trim();
        return $"https://{id[..Math.Min(4, id.Length)]}.{kind}.greenapi.com/waInstance{id}/{method}/{settings.ApiKey!.Trim()}";
    }

    private string WaApiUrl(string action) => $"https://waapi.app/api/v1/instances/{settings.Instance!.Trim()}/client/action/{action}";

    public async Task<string?> WhatsAppAsync(string number, string text, MessageFile? file, CancellationToken cancel)
    {
        if (settings.WhatsAppProvider == MessageSettings.WppConnect) return await WppSendAsync(number, text, file, cancel);
        var chat = $"{number}@c.us";
        using var request = settings.WhatsAppProvider == MessageSettings.WaApi ? WaApiRequest(chat, text, file) : GreenRequest(chat, text, file);
        var reply = await SendAsync(request, cancel);
        return JsonNode.Parse(reply)?["idMessage"]?.GetValue<string>();
    }

    private HttpRequestMessage WppRequest(HttpMethod method, string action, object? body = null)
    {
        var url = $"{settings.ServerUrl!.Trim().TrimEnd('/')}/api/{Uri.EscapeDataString(settings.Instance!.Trim())}/{action}";
        var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey!.Trim());
        return request;
    }

    /// <summary>The own WPPConnect gateway: send-message for text, send-file with a base64 data URL for a voucher.</summary>
    private async Task<string?> WppSendAsync(string number, string text, MessageFile? file, CancellationToken cancel)
    {
        object body = file is null
            ? new { phone = number, isGroup = false, isNewsletter = false, isLid = false, message = text }
            : new { phone = number, isGroup = false, isNewsletter = false, isLid = false, filename = file.Name, caption = text, base64 = $"data:application/pdf;base64,{Convert.ToBase64String(file.Content)}" };
        using var request = WppRequest(HttpMethod.Post, file is null ? "send-message" : "send-file", body);
        var reply = JsonNode.Parse(await SendAsync(request, cancel));
        // The gateway can answer 200/201 with status "error".
        if (reply?["status"]?.ToString() is { } status && !status.Equals("success", StringComparison.OrdinalIgnoreCase))
            throw new MessageSendException(reply["message"]?.ToString() ?? $"The gateway answered {status}.");
        var first = reply?["response"] is JsonArray list && list.Count > 0 ? list[0] : reply?["response"];
        return first is JsonObject o ? (o["id"] as JsonValue)?.ToString() ?? o["id"]?["_serialized"]?.ToString() : null;
    }

    private HttpRequestMessage GreenRequest(string chat, string text, MessageFile? file)
    {
        if (file is null)
            return new HttpRequestMessage(HttpMethod.Post, GreenUrl("api", "sendMessage")) { Content = JsonContent.Create(new { chatId = chat, message = text }) };
        var form = new MultipartFormDataContent
        {
            { new StringContent(chat), "chatId" },
            { new StringContent(file.Name), "fileName" },
            { new StringContent(text), "caption" },
            { new ByteArrayContent(file.Content), "file", file.Name },
        };
        return new HttpRequestMessage(HttpMethod.Post, GreenUrl("media", "sendFileByUpload")) { Content = form };
    }

    private HttpRequestMessage WaApiRequest(string chat, string text, MessageFile? file)
    {
        object body = file is null
            ? new { chatId = chat, message = text }
            : new { chatId = chat, mediaBase64 = Convert.ToBase64String(file.Content), mediaName = file.Name, mediaCaption = text };
        var request = new HttpRequestMessage(HttpMethod.Post, WaApiUrl(file is null ? "send-message" : "send-media")) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey!.Trim());
        return request;
    }

    /// <summary>Whether the WhatsApp account is connected. Sends no message.</summary>
    public async Task<string> WhatsAppStateAsync(CancellationToken cancel)
    {
        if (settings.WhatsAppProvider == MessageSettings.WaApi) return "Test this provider by sending to your test number.";
        if (settings.WhatsAppProvider == MessageSettings.WppConnect)
        {
            using var check = WppRequest(HttpMethod.Get, "check-connection-session");
            var state = JsonNode.Parse(await SendAsync(check, cancel));
            return state?["message"]?.ToString() ?? state?["status"]?.ToString() ?? "unknown";
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, GreenUrl("api", "getStateInstance"));
        var reply = await SendAsync(request, cancel);
        return JsonNode.Parse(reply)?["stateInstance"]?.GetValue<string>() ?? "unknown";
    }

    public async Task<string?> EmailAsync(string to, string subject, string text, MessageFile? file, CancellationToken cancel)
    {
        if (settings.EmailSignIn == MessageSettings.Smtp) await SmtpAsync(to, subject, text, file, cancel);
        else await GraphAsync(to, subject, text, file, cancel);
        return null;
    }

    private async Task SmtpAsync(string to, string subject, string text, MessageFile? file, CancellationToken cancel)
    {
        using var mail = new MailMessage(settings.SendFrom!.Trim(), to, subject, text);
        if (!string.IsNullOrWhiteSpace(settings.ReplyTo)) mail.ReplyToList.Add(settings.ReplyTo.Trim());
        using var stream = file is null ? null : new MemoryStream(file.Content);
        if (file is not null) mail.Attachments.Add(new Attachment(stream!, file.Name));
        using var client = new SmtpClient(settings.SmtpServer!.Trim(), settings.SmtpPort) { EnableSsl = true };
        if (!string.IsNullOrWhiteSpace(settings.SmtpUser))
            client.Credentials = new System.Net.NetworkCredential(settings.SmtpUser.Trim(), settings.SmtpPassword);
        try { await client.SendMailAsync(mail, cancel); }
        catch (SmtpException ex) { throw new MessageSendException(ex.Message, ex); }
    }

    private async Task GraphAsync(string to, string subject, string text, MessageFile? file, CancellationToken cancel)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{settings.TenantId!.Trim()}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId!.Trim(), ["client_secret"] = settings.ClientSecret!.Trim(),
                ["scope"] = "https://graph.microsoft.com/.default", ["grant_type"] = "client_credentials",
            }),
        };
        var token = JsonNode.Parse(await SendAsync(tokenRequest, cancel))?["access_token"]?.GetValue<string>()
                    ?? throw new MessageSendException("Microsoft 365 gave no sign-in token.");
        var message = new JsonObject
        {
            ["subject"] = subject,
            ["body"] = new JsonObject { ["contentType"] = "Text", ["content"] = text },
            ["toRecipients"] = Recipients(to),
        };
        if (!string.IsNullOrWhiteSpace(settings.ReplyTo)) message["replyTo"] = Recipients(settings.ReplyTo.Trim());
        if (file is not null)
            message["attachments"] = new JsonArray(new JsonObject
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment", ["name"] = file.Name,
                ["contentType"] = "application/pdf", ["contentBytes"] = Convert.ToBase64String(file.Content),
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(settings.SendFrom!.Trim())}/sendMail")
        {
            Content = JsonContent.Create(new JsonObject { ["message"] = message }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        await SendAsync(request, cancel);
    }

    private static JsonArray Recipients(string address) =>
        new(new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = address } });

    private static async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        try
        {
            using var response = await Http.SendAsync(request, cancel);
            var body = await response.Content.ReadAsStringAsync(cancel);
            if (response.IsSuccessStatusCode) return body;
            throw new MessageSendException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body[..Math.Min(body.Length, 300)]}");
        }
        catch (HttpRequestException ex) { throw new MessageSendException($"Could not reach the service: {ex.Message}", ex); }
        catch (TaskCanceledException ex) when (!cancel.IsCancellationRequested) { throw new MessageSendException("The service took too long to answer.", ex); }
    }
}
