/* Messages: wording an Admin has changed, the log of everything sent, and an email address for staff. Safe to re-run.
   A message type with no row in tblMessageTemplates uses the wording built into the app.
   The old Access log, WAMessages, is left as it is. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.Staff', 'Email') IS NULL
    ALTER TABLE dbo.Staff ADD Email nvarchar(100) NULL;
GO

IF OBJECT_ID('dbo.tblMessageTemplates') IS NULL
    CREATE TABLE dbo.tblMessageTemplates (
        TemplateKey varchar(30)   NOT NULL CONSTRAINT PK_tblMessageTemplates PRIMARY KEY,
        Subject     nvarchar(200) NOT NULL,
        Body        nvarchar(max) NOT NULL,
        UpdatedBy   nvarchar(128) NULL,
        UpdatedOn   datetime2(0)  NULL
    );
GO

IF OBJECT_ID('dbo.tblMessageLog') IS NULL
BEGIN
    CREATE TABLE dbo.tblMessageLog (
        LogID      int           IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblMessageLog PRIMARY KEY,
        SentOn     datetime2(0)  NOT NULL,
        SentBy     nvarchar(128) NOT NULL,
        Kind       varchar(30)   NOT NULL,
        RefKey     nvarchar(50)  NOT NULL,   -- the date, month or fee periods the message is about
        PersonID   int           NOT NULL,   -- the student's CC or the staff member's ID
        PersonName nvarchar(150) NOT NULL,
        Channel    varchar(10)   NOT NULL,   -- WhatsApp or Email
        SentTo     nvarchar(200) NOT NULL,
        Body       nvarchar(max) NOT NULL,
        FileName   nvarchar(200) NULL,
        Status     varchar(10)   NOT NULL,   -- Sent, Failed, or Test (went to the test number or address instead)
        Detail     nvarchar(500) NULL        -- the provider's message id, or why it failed
    );
    CREATE INDEX IX_tblMessageLog_Kind ON dbo.tblMessageLog (Kind, RefKey, PersonID) INCLUDE (Channel, Status, SentOn);
    CREATE INDEX IX_tblMessageLog_SentOn ON dbo.tblMessageLog (SentOn DESC);
END
GO
