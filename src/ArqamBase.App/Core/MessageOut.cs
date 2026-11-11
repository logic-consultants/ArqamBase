using ArqamBase.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace ArqamBase.App.Core;

/// <summary>How a message goes out. Stored in tblMessageLog.Channel as WhatsApp, Email or SMS.</summary>
public enum MessageChannel { WhatsApp, Email, Sms }

/// <summary>
/// Sends messages and logs each one (tblMessageLog), for the Messages screen and for single messages from other screens
/// (a fee receipt from the ledger, a message to a student's or family's parents).
/// On any database but the live one, and while test mode is on, messages go to the test number and address instead.
/// </summary>
public static class MessageOut
{
    public const string LiveDatabase = "ArqamBase_be";

    /// <summary>Test messages go to the test number and address on any database but the live one.</summary>
    public static bool MustTest(AppConfig config) => !string.Equals(config.Database, LiveDatabase, StringComparison.OrdinalIgnoreCase);

    public static bool IsTest(AppConfig config, MessageSettings settings) => settings.TestMode || MustTest(config);

    public static string Name(MessageChannel channel) => channel == MessageChannel.Sms ? "SMS" : channel.ToString();

    /// <summary>Why a message cannot go by these channels on this computer, or null when it can.</summary>
    public static string? Problem(AppConfig config, MessageSettings settings, IReadOnlyCollection<MessageChannel> channels)
    {
        if (channels.Contains(MessageChannel.WhatsApp) && !settings.WhatsAppReady) return "WhatsApp is not set up on this computer. An Admin adds it in Messages › Message settings.";
        if (channels.Contains(MessageChannel.Email) && !settings.EmailReady) return "Email is not set up on this computer. An Admin adds it in Messages › Message settings.";
        if (channels.Contains(MessageChannel.Sms) && !settings.SmsReady) return "SMS is not set up on this computer. An Admin adds the SMS phone in Messages › Message settings.";
        if (IsTest(config, settings) && channels.Any(c => To(new MessageRow(), c, test: true, settings) is null))
            return "Messages are in test mode, so they go only to the test number and test email. Add them in Messages › Message settings.";
        return null;
    }

    /// <summary>Where a message goes: the person, or the test number or address in test mode (null when there is none).</summary>
    public static string? To(MessageRow row, MessageChannel channel, bool test, MessageSettings settings) => channel switch
    {
        MessageChannel.Email => test ? settings.TestEmail is { Length: > 0 } t ? t.Trim() : null : row.Email,
        _ => test ? MessageText.WhatsAppNumber(settings.TestNumber) : row.Number,
    };

    /// <summary>Sends one message and logs it, whatever happens. Returns Sent, Test or Failed, and the provider's id or why it failed.</summary>
    public static async Task<(string Status, string? Detail)> SendOneAsync(AppConfig config, UserSession session, IMessageSender sender, MessageRow row,
        MessageChannel channel, string to, string subject, string body, MessageFile? file, bool test, CancellationToken stop)
    {
        // WhatsApp shows *bold*; email and SMS do not. An SMS carries no file.
        var text = channel == MessageChannel.WhatsApp ? body : MessageText.ForEmail(body);
        if (channel == MessageChannel.Sms) file = null;
        string status;
        string? detail;
        try
        {
            detail = channel switch
            {
                MessageChannel.WhatsApp => await sender.WhatsAppAsync(to, text, file, stop),
                MessageChannel.Email => await sender.EmailAsync(to, subject, text, file, stop),
                _ => await sender.SmsAsync(to, text, stop),
            };
            status = test ? "Test" : "Sent";
        }
        catch (MessageSendException ex) { (status, detail) = ("Failed", ex.Message); }
        catch (HttpRequestException ex) { (status, detail) = ("Failed", ex.Message); }

        try
        {
            using var db = config.CreateContext(session);
            db.MessageLogs.Add(new MessageLog
            {
                SentOn = DateTime.Now, SentBy = session.UserName, Kind = row.Kind.ToString(), RefKey = row.RefKey[..Math.Min(row.RefKey.Length, 50)], PersonId = row.Id,
                PersonName = row.Name[..Math.Min(row.Name.Length, 150)], Channel = Name(channel), SentTo = to, Body = text, FileName = file?.Name,
                Status = status, Detail = detail?[..Math.Min(detail.Length, 500)],
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateException ex) { Program.ShowError("A message went out but could not be logged.", ex); }
        return (status, detail);
    }

    /// <summary>
    /// Sends one message by each of <paramref name="channels"/>, with its file where the channel takes one. Returns what happened,
    /// one line for each channel, e.g. "WhatsApp sent to +923001234567." The caller has checked <see cref="Problem"/>.
    /// </summary>
    public static async Task<(bool Ok, string Result)> SendAsync(AppConfig config, UserSession session, MessageRow row, IReadOnlyCollection<MessageChannel> channels,
        string? subject = null, string? body = null, IMessageSender? sender = null, CancellationToken stop = default)
    {
        var settings = MessageSettings.Load();
        var test = IsTest(config, settings);
        sender ??= new MessageSender(settings);
        var needsFile = row.Document is not null && channels.Any(c => c != MessageChannel.Sms);
        var file = needsFile ? await Task.Run(() => new MessageFile(row.FileName!, row.Document!().GeneratePdf()), stop) : null;
        var lines = new List<string>();
        var ok = true;
        foreach (var channel in new[] { MessageChannel.WhatsApp, MessageChannel.Email, MessageChannel.Sms }.Where(channels.Contains))
        {
            var name = Name(channel);
            if (To(row, channel, test, settings) is not { Length: > 0 } to)
            {
                lines.Add($"{name}: no {(channel == MessageChannel.Email ? "email address" : "number")}.");
                ok = false;
                continue;
            }
            var (status, detail) = await SendOneAsync(config, session, sender, row, channel, to, subject ?? row.Subject, body ?? row.Body, file, test, stop);
            var where = channel == MessageChannel.Email ? to : $"+{to}";
            lines.Add(status == "Failed" ? $"{name} to {where} failed: {detail}" : $"{name} {(test ? "sent (test) to" : "sent to")} {where}.");
            ok &= status != "Failed";
        }
        return (ok, string.Join("\n", lines));
    }
}
