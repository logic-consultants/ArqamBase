/* Staff, salary sheet and advances. Run as a database owner after attendance_roles_install.sql. Safe to re-run.
   tblSalaries and tblSalariesHead already exist and are empty; they get the columns the monthly sheet needs.
   New Staff columns are optional, so Access keeps working with the table. */

-- 1. Staff: campus, which salary sheet they are on, how they are paid
IF COL_LENGTH('dbo.Staff', 'BranchID') IS NULL
    ALTER TABLE dbo.Staff ADD BranchID int NULL CONSTRAINT FK_Staff_Branches REFERENCES dbo.Branches (BrID);
IF COL_LENGTH('dbo.Staff', 'SalarySheet') IS NULL
    ALTER TABLE dbo.Staff ADD SalarySheet tinyint NULL; -- 1 Teaching (Girls), 2 Teaching (Boys), 3 Non Teaching, 4 Directors
IF COL_LENGTH('dbo.Staff', 'PayMethod') IS NULL
    ALTER TABLE dbo.Staff ADD PayMethod nvarchar(20) NULL; -- BAHL or CASH
IF COL_LENGTH('dbo.Staff', 'BankAccount') IS NULL
    ALTER TABLE dbo.Staff ADD BankAccount nvarchar(50) NULL;
GO

-- 2. One sheet per month
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblSalariesHead') AND type = 'PK')
    ALTER TABLE dbo.tblSalariesHead ADD CONSTRAINT PK_tblSalariesHead PRIMARY KEY (SalHeadID);
GO
IF COL_LENGTH('dbo.tblSalariesHead', 'IsFinal') IS NULL
    ALTER TABLE dbo.tblSalariesHead ADD
        IsFinal      bit           NOT NULL CONSTRAINT DF_SalHead_IsFinal DEFAULT (0),
        BankChequeNo nvarchar(30)  NULL,
        CashChequeNo nvarchar(30)  NULL,
        PreparedBy   nvarchar(128) NULL,
        PreparedOn   datetime2(0)  NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_SalHead_Period')
    CREATE UNIQUE INDEX UQ_SalHead_Period ON dbo.tblSalariesHead (Period) WHERE Period IS NOT NULL; -- yyyyMM
GO

-- 3. One row per person per sheet
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID('dbo.tblSalaries') AND type = 'PK')
    ALTER TABLE dbo.tblSalaries ADD CONSTRAINT PK_tblSalaries PRIMARY KEY (SalID);
GO
IF COL_LENGTH('dbo.tblSalaries', 'fk_SalHeadID') IS NULL
    ALTER TABLE dbo.tblSalaries ADD
        fk_SalHeadID      int           NULL CONSTRAINT FK_Salaries_Head REFERENCES dbo.tblSalariesHead (SalHeadID) ON DELETE CASCADE,
        SalarySheet       tinyint       NULL,
        BasicSalary       money         NULL,
        RemainingSecurity nvarchar(20)  NULL,
        Verify            nvarchar(30)  NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Salaries_Staff')
    ALTER TABLE dbo.tblSalaries ADD CONSTRAINT FK_Salaries_Staff FOREIGN KEY (fk_EmpID) REFERENCES dbo.Staff (TeachersID);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Salaries_Head_Emp')
    CREATE UNIQUE INDEX UQ_Salaries_Head_Emp ON dbo.tblSalaries (fk_SalHeadID, fk_EmpID);
GO

-- 4. Advances (the Advance Request Form). What is left of an advance comes from the amounts deducted on the salary sheets.
IF OBJECT_ID('dbo.tblStaffAdvance') IS NULL
BEGIN
    CREATE TABLE dbo.tblStaffAdvance (
        AdvanceID     int           NOT NULL IDENTITY CONSTRAINT PK_tblStaffAdvance PRIMARY KEY,
        AdvanceDate   date          NOT NULL,
        TeachersID    int           NOT NULL CONSTRAINT FK_StaffAdvance_Staff REFERENCES dbo.Staff (TeachersID),
        Amount        money         NOT NULL,
        Installments  int           NOT NULL CONSTRAINT DF_StaffAdvance_Inst DEFAULT (1),
        FirstPeriod   nvarchar(6)   NOT NULL, -- yyyyMM of the first salary it is deducted from
        Witness1      nvarchar(100) NULL,
        Witness2      nvarchar(100) NULL,
        Witness3      nvarchar(100) NULL,
        PaidThrough   nvarchar(100) NULL,
        RecommendedBy nvarchar(100) NULL,
        ApprovedBy    nvarchar(100) NULL,
        Remarks       nvarchar(500) NULL,
        CreatedBy     nvarchar(128) NOT NULL CONSTRAINT DF_StaffAdvance_By DEFAULT (ORIGINAL_LOGIN()),
        isDeleted     bit           NOT NULL CONSTRAINT DF_StaffAdvance_Del DEFAULT (0),
        CreatedOn     datetime      NULL CONSTRAINT DF_StaffAdvance_On DEFAULT (GETDATE()),
        DeletedOn     datetime      NULL
    );
    CREATE INDEX IX_StaffAdvance_Staff ON dbo.tblStaffAdvance (TeachersID);
END
GO
