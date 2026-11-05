/* Staff pictures and documents: one profile picture and up to 10 documents per person. Safe to re-run.
   The files are kept in the shared folder \\das-svr\db_share\arqambase\StaffAttachments, named [Employee ID]_[Name]_[Type],
   e.g. 112_AMINA BIBI_ProfilePicture.jpg. This table holds the path of each file.
   DocType: ProfilePicture, CNIC, CV, Degree, ExperienceLetter, AppointmentLetter, or Doc1, Doc2… for other documents.
   Deleted files are moved to the Deleted subfolder and the row is marked isDeleted. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.tblStaffDocuments') IS NULL
BEGIN
    CREATE TABLE dbo.tblStaffDocuments (
        DocID            int            IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblStaffDocuments PRIMARY KEY,
        TeachersID       int            NOT NULL CONSTRAINT FK_tblStaffDocuments_Staff REFERENCES dbo.Staff (TeachersID),
        DocType          varchar(30)    NOT NULL,
        FilePath         nvarchar(400)  NOT NULL,
        OriginalFileName nvarchar(260)  NULL,
        FileSizeKB       int            NULL,
        Notes            nvarchar(255)  NULL,
        CreatedBy        nvarchar(50)   NULL,
        CreatedOn        datetime       NOT NULL CONSTRAINT DF_tblStaffDocuments_CreatedOn DEFAULT (GETDATE()),
        isDeleted        bit            NOT NULL CONSTRAINT DF_tblStaffDocuments_isDeleted DEFAULT (0),
        DeletedBy        nvarchar(50)   NULL,
        DeletedOn        datetime       NULL,
        TS               rowversion     NOT NULL
    );
END
GO
-- One file of each type per person (Doc1, Doc2… are different types).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblStaffDocuments_Staff_Type' AND object_id = OBJECT_ID('dbo.tblStaffDocuments'))
    CREATE UNIQUE INDEX UX_tblStaffDocuments_Staff_Type ON dbo.tblStaffDocuments (TeachersID, DocType) WHERE isDeleted = 0;
GO
