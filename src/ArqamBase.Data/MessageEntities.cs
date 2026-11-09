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
