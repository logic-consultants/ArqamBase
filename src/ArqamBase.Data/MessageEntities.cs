namespace ArqamBase.Data;

/// <summary>The wording of a message type changed by an Admin (table tblMessageTemplates).</summary>
public class MessageTemplate
{
    public string Key { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

/// <summary>One message sent, or tried, on one channel (table tblMessageLog).</summary>
public class MessageLog
{
    public int Id { get; set; }
    public DateTime SentOn { get; set; }
    public string SentBy { get; set; } = "";
    public string Kind { get; set; } = "";
    /// <summary>The date, month or fee periods the message is about.</summary>
    public string RefKey { get; set; } = "";
    public int PersonId { get; set; }
    public string PersonName { get; set; } = "";
    public string Channel { get; set; } = "";
    public string SentTo { get; set; } = "";
    public string Body { get; set; } = "";
    public string? FileName { get; set; }
    /// <summary>Sent, Failed, or Test (it went to the test number or address instead).</summary>
    public string Status { get; set; } = "";
    public string? Detail { get; set; }
}

/// <summary>The Google account the student contacts are kept in, and its sign-in (table tblGoogleOAuth, one row).</summary>
public class GoogleOAuth
{
    public int Id { get; set; }
    public string? GoogleAccount { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? RefreshToken { get; set; }
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public string? Scope { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUpdated { get; set; }
}
