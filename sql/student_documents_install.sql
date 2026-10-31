/* Student pictures and documents (up to 10 per student). Safe to re-run.
   The files are kept in the shared folder \\das-svr\arqambase\StudentsAttachments, named [CC]_[Student name]_[Type],
   e.g. 2562_MARYAM NAEEM_ProfilePicture.jpg. This table holds the path of each file.
   DocType: ProfilePicture, FormB, BirthCertificate, FatherCNIC, MotherCNIC, LeavingCertificate, ResultCard, or Doc1, Doc2… for other documents.
   Deleted files are moved to the Deleted subfolder and the row is marked isDeleted. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.tblStudentDocuments') IS NULL
BEGIN
    CREATE TABLE dbo.tblStudentDocuments (
        DocID            int            IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblStudentDocuments PRIMARY KEY,
        CC               int            NOT NULL CONSTRAINT FK_tblStudentDocuments_Students REFERENCES dbo.Students (CC),
        DocType          varchar(30)    NOT NULL,
        FilePath         nvarchar(400)  NOT NULL,
        OriginalFileName nvarchar(260)  NULL,
        FileSizeKB       int            NULL,
        Notes            nvarchar(255)  NULL,
        CreatedBy        nvarchar(50)   NULL,
        CreatedOn        datetime       NOT NULL CONSTRAINT DF_tblStudentDocuments_CreatedOn DEFAULT (GETDATE()),
        isDeleted        bit            NOT NULL CONSTRAINT DF_tblStudentDocuments_isDeleted DEFAULT (0),
        DeletedBy        nvarchar(50)   NULL,
        DeletedOn        datetime       NULL,
        TS               rowversion     NOT NULL
    );
END
GO
-- One file of each type per student (Doc1, Doc2… are different types).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblStudentDocuments_CC_Type' AND object_id = OBJECT_ID('dbo.tblStudentDocuments'))
    CREATE UNIQUE INDEX UX_tblStudentDocuments_CC_Type ON dbo.tblStudentDocuments (CC, DocType) WHERE isDeleted = 0;
GO
