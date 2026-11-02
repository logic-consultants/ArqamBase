/* Bank / online deposit slips attached to fee receipts (Online Deposit Girls and Boys). Safe to re-run.
   A receipt is known by its collection type, receipt number and date (as in Feerecipt: CollectionType, RecipNo, ReDate).
   The files are kept in the FeeReceipts subfolder of \\das-svr\arqambase\StudentsAttachments,
   named [yyyy-MM-dd]_[type code]_[receipt no.], e.g. 2026-11-01_ODG_953.jpg. This table holds the path of each file.
   A replaced or removed slip is moved to FeeReceipts\Deleted and its row is marked isDeleted. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.tblFeeReceiptFiles') IS NULL
BEGIN
    CREATE TABLE dbo.tblFeeReceiptFiles (
        FileID           int            IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblFeeReceiptFiles PRIMARY KEY,
        CollectionType   int            NOT NULL CONSTRAINT FK_tblFeeReceiptFiles_CollectionTypes REFERENCES dbo.tblCollectionTypes (CollectionTypeID),
        ReceiptNo        int            NOT NULL,
        ReceivedOn       date           NOT NULL,
        FilePath         nvarchar(400)  NOT NULL,
        OriginalFileName nvarchar(260)  NULL,
        FileSizeKB       int            NULL,
        CreatedBy        nvarchar(50)   NULL,
        CreatedOn        datetime       NOT NULL CONSTRAINT DF_tblFeeReceiptFiles_CreatedOn DEFAULT (GETDATE()),
        isDeleted        bit            NOT NULL CONSTRAINT DF_tblFeeReceiptFiles_isDeleted DEFAULT (0),
        DeletedBy        nvarchar(50)   NULL,
        DeletedOn        datetime       NULL,
        TS               rowversion     NOT NULL
    );
END
GO
-- One current slip per receipt.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tblFeeReceiptFiles_Receipt' AND object_id = OBJECT_ID('dbo.tblFeeReceiptFiles'))
    CREATE UNIQUE INDEX UX_tblFeeReceiptFiles_Receipt ON dbo.tblFeeReceiptFiles (CollectionType, ReceiptNo, ReceivedOn) WHERE isDeleted = 0;
GO
