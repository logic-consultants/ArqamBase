/* Accounts (phase 8): account types, sub-types, accounts, financial years, vouchers and voucher lines,
   and the Accountant role. Safe to re-run. The 2026-27 data from the Access accounts database
   (ACC_Toba_be_26-27.accdb) is loaded by accounts_import_2026_27.sql, made with sql/tools/import-accounts.ps1.

   Debit and Credit replace Access's DepositAmount ("D") and WithdrawalAmount ("W").
   A balance is Debit - Credit: positive is Dr, negative is Cr. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- The Accountant role (vouchers, fee posting, accounting reports).
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AppUsers_Role'
           AND definition NOT LIKE '%Accountant%')
BEGIN
    ALTER TABLE dbo.AppUsers DROP CONSTRAINT CK_AppUsers_Role;
    ALTER TABLE dbo.AppUsers ADD CONSTRAINT CK_AppUsers_Role
        CHECK (RoleName IN ('Admin','FeeClerk','Admissions','ReadOnly','Coordinator','TeacherInCharge','Accountant'));
END
GO

IF OBJECT_ID('dbo.acc_AccountTypes') IS NULL
BEGIN
    CREATE TABLE dbo.acc_AccountTypes (
        AccountTypeID int          NOT NULL CONSTRAINT PK_acc_AccountTypes PRIMARY KEY,
        Name          nvarchar(50) NOT NULL,
        NormalSide    char(2)      NOT NULL CONSTRAINT CK_acc_AccountTypes_Side CHECK (NormalSide IN ('Dr','Cr')),
        SortOrder     int          NOT NULL
    );
    INSERT dbo.acc_AccountTypes (AccountTypeID, Name, NormalSide, SortOrder) VALUES
        (1, N'Assets', 'Dr', 1), (2, N'Liabilities', 'Cr', 2), (3, N'Income', 'Cr', 3), (4, N'Expenses', 'Dr', 4);
END
GO

IF OBJECT_ID('dbo.acc_SubTypes') IS NULL
BEGIN
    CREATE TABLE dbo.acc_SubTypes (
        SubTypeID     int          NOT NULL CONSTRAINT PK_acc_SubTypes PRIMARY KEY,
        Name          nvarchar(50) NOT NULL,
        AccountTypeID int          NULL CONSTRAINT FK_acc_SubTypes_Type REFERENCES dbo.acc_AccountTypes (AccountTypeID)
    );
    -- The Access sub-types, with the type each belongs to.
    INSERT dbo.acc_SubTypes (SubTypeID, Name, AccountTypeID) VALUES
        (1, N'Capital', 2), (2, N'Receivables', 1), (3, N'Payables', 2), (4, N'Fixed assets', 1), (5, N'Cash & banks', 1);
END
GO

IF OBJECT_ID('dbo.acc_Accounts') IS NULL
    CREATE TABLE dbo.acc_Accounts (
        AccountID     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_acc_Accounts PRIMARY KEY,
        AccountNo     varchar(10)    NOT NULL CONSTRAINT UQ_acc_Accounts_No UNIQUE,
        Name          nvarchar(100)  NOT NULL,
        AccountTypeID int            NOT NULL CONSTRAINT FK_acc_Accounts_Type REFERENCES dbo.acc_AccountTypes (AccountTypeID),
        SubTypeID     int            NULL CONSTRAINT FK_acc_Accounts_SubType REFERENCES dbo.acc_SubTypes (SubTypeID),
        Kind          tinyint        NOT NULL CONSTRAINT DF_acc_Accounts_Kind DEFAULT 0
                                     CONSTRAINT CK_acc_Accounts_Kind CHECK (Kind IN (0,1,2)),   -- 0 ordinary, 1 cash, 2 bank
        BankAccountNo nvarchar(50)   NULL,
        CareOf        nvarchar(100)  NULL,
        Mobile        nvarchar(30)   NULL,
        Mobile2       nvarchar(30)   NULL,
        Phone         nvarchar(30)   NULL,
        Phone2        nvarchar(30)   NULL,
        Address       nvarchar(255)  NULL,
        Notes         nvarchar(500)  NULL,
        IsActive      bit            NOT NULL CONSTRAINT DF_acc_Accounts_Active DEFAULT 1,
        CreatedBy     nvarchar(128)  NULL,
        CreatedOn     datetime2(0)   NULL,
        UpdatedBy     nvarchar(128)  NULL,
        UpdatedOn     datetime2(0)   NULL,
        TS            rowversion     NOT NULL
    );
GO

IF OBJECT_ID('dbo.acc_FinancialYears') IS NULL
    CREATE TABLE dbo.acc_FinancialYears (
        YearID     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_acc_FinancialYears PRIMARY KEY,
        Name       varchar(10)  NOT NULL CONSTRAINT UQ_acc_FinancialYears_Name UNIQUE,   -- e.g. 2026-27
        StartDate  date         NOT NULL,
        EndDate    date         NOT NULL,
        LockedUpTo date         NULL,    -- vouchers on or before this day are locked; only an Admin changes them
        IsClosed   bit          NOT NULL CONSTRAINT DF_acc_FinancialYears_Closed DEFAULT 0
    );
GO

IF OBJECT_ID('dbo.acc_Vouchers') IS NULL
BEGIN
    CREATE TABLE dbo.acc_Vouchers (
        VoucherID   int IDENTITY(1,1) NOT NULL CONSTRAINT PK_acc_Vouchers PRIMARY KEY,
        YearID      int           NOT NULL CONSTRAINT FK_acc_Vouchers_Year REFERENCES dbo.acc_FinancialYears (YearID),
        VoucherNo   int           NOT NULL,
        VoucherType varchar(10)   NOT NULL CONSTRAINT CK_acc_Vouchers_Type CHECK (VoucherType IN ('Daily','Bank','Journal','Opening')),
        VoucherDate date          NOT NULL,
        Folio       int           NULL,
        Narration   nvarchar(255) NULL,
        CreatedBy   nvarchar(128) NULL,
        CreatedOn   datetime2(0)  NULL,
        UpdatedBy   nvarchar(128) NULL,
        UpdatedOn   datetime2(0)  NULL,
        TS          rowversion    NOT NULL,
        CONSTRAINT UQ_acc_Vouchers_No UNIQUE (YearID, VoucherNo)
    );
    CREATE INDEX IX_acc_Vouchers_Date ON dbo.acc_Vouchers (VoucherDate);
END
GO

IF OBJECT_ID('dbo.acc_VoucherLines') IS NULL
BEGIN
    CREATE TABLE dbo.acc_VoucherLines (
        LineID      int IDENTITY(1,1) NOT NULL CONSTRAINT PK_acc_VoucherLines PRIMARY KEY,
        VoucherID   int            NULL CONSTRAINT FK_acc_VoucherLines_Voucher REFERENCES dbo.acc_Vouchers (VoucherID) ON DELETE CASCADE,
        LineNumber  int            NOT NULL,
        AccountID   int            NOT NULL CONSTRAINT FK_acc_VoucherLines_Account REFERENCES dbo.acc_Accounts (AccountID),
        Description nvarchar(255)  NULL,
        BillNo      nvarchar(20)   NULL,
        ChequeNo    nvarchar(30)   NULL,
        ChequeDate  date           NULL,
        Debit       decimal(14,2)  NOT NULL CONSTRAINT DF_acc_VoucherLines_Debit DEFAULT 0,
        Credit      decimal(14,2)  NOT NULL CONSTRAINT DF_acc_VoucherLines_Credit DEFAULT 0,
        IsOk        bit            NOT NULL CONSTRAINT DF_acc_VoucherLines_Ok DEFAULT 0,       -- the line has been checked
        IsCleared   bit            NOT NULL CONSTRAINT DF_acc_VoucherLines_Cleared DEFAULT 0,  -- the cheque or bank entry went through
        Source      varchar(10)    NOT NULL CONSTRAINT DF_acc_VoucherLines_Source DEFAULT 'Typed'
                                   CONSTRAINT CK_acc_VoucherLines_Source CHECK (Source IN ('Typed','Fees','Auto','Import')),
        AccessID    int            NULL,   -- table1.TransactionID of a line imported from Access
        EnteredBy   nvarchar(128)  NULL,   -- who typed the line (empty for imported lines)
        EnteredOn   datetime2(0)   NULL,
        ChangedBy   nvarchar(128)  NULL,   -- who last changed it after it was first saved
        ChangedOn   datetime2(0)   NULL,
        CONSTRAINT CK_acc_VoucherLines_Amount CHECK (Debit >= 0 AND Credit >= 0 AND NOT (Debit > 0 AND Credit > 0))
    );
    -- A line with no voucher can only come from Access (one such line exists in 2026-27).
    CREATE INDEX IX_acc_VoucherLines_Voucher ON dbo.acc_VoucherLines (VoucherID, LineNumber);
    CREATE INDEX IX_acc_VoucherLines_Account ON dbo.acc_VoucherLines (AccountID) INCLUDE (Debit, Credit, VoucherID);
END
GO

-- Who entered and changed each line (added after the first install).
IF COL_LENGTH('dbo.acc_VoucherLines', 'EnteredBy') IS NULL
    ALTER TABLE dbo.acc_VoucherLines ADD EnteredBy nvarchar(128) NULL, EnteredOn datetime2(0) NULL,
                                         ChangedBy nvarchar(128) NULL, ChangedOn datetime2(0) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.acc_FinancialYears)
    INSERT dbo.acc_FinancialYears (Name, StartDate, EndDate) VALUES ('2026-27', '2026-07-01', '2027-06-30');
GO
