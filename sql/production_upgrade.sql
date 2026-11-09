/* ArqamBase production upgrade: everything the new desktop app needs in ArqamBase_be.
   Built from the 12 install scripts in this folder, in the order they must run. Each part is safe to re-run.
   Run on DAS-SVR as sysadmin, after a full backup, while nobody is using Access or the app.
   Adds tables, columns, indexes, a view and audit triggers only. No existing data is changed or deleted,
   except that attendance_install copies the old Access attendance trial data into the new attendance tables.
   Not included on purpose: create_dev_database, attendance_remove_old (Access still uses the old tables),
   audit_remove, windows_logins_template (only for a new person). */
USE [ArqamBase_be];
GO
IF DB_NAME() <> N'ArqamBase_be'
    RAISERROR (N'Not connected to ArqamBase_be. Stop and check the database.', 20, 1) WITH LOG;
GO
SET NOCOUNT ON;
GO


/* ==================== 1 of 12: app_users_install.sql ==================== */
PRINT N'1 of 12: app_users_install';
GO
/* App users for the new desktop app. Safe to re-run.
   Users sign in to the app with a user name and password (stored as a salted PBKDF2 hash, never the password itself),
   then connect to SQL Server with their own Windows account on DAS-SVR.
   Roles: Admin, FeeClerk, Admissions, ReadOnly, Coordinator, TeacherInCharge (see attendance_roles_install.sql). When the table is empty, the first person to sign in creates the Admin account. */

-- Replace the first version of the table (keyed on Windows login), which only ever existed on ArqamBase_dev.
IF COL_LENGTH('dbo.AppUsers', 'WindowsLogin') IS NOT NULL
    DROP TABLE dbo.AppUsers;
GO

IF OBJECT_ID('dbo.AppUsers') IS NULL
    CREATE TABLE dbo.AppUsers (
        UserName           nvarchar(50)   NOT NULL CONSTRAINT PK_AppUsers PRIMARY KEY,
        FullName           nvarchar(100)  NULL,
        RoleName           varchar(20)    NOT NULL CONSTRAINT CK_AppUsers_Role CHECK (RoleName IN ('Admin','FeeClerk','Admissions','ReadOnly','Coordinator','TeacherInCharge')),
        PasswordHash       varbinary(32)  NULL,
        PasswordSalt       varbinary(16)  NULL,
        PasswordIterations int            NULL,
        MustChangePassword bit            NOT NULL CONSTRAINT DF_AppUsers_MustChange DEFAULT (1),
        IsActive           bit            NOT NULL CONSTRAINT DF_AppUsers_IsActive DEFAULT (1),
        FailedAttempts     int            NOT NULL CONSTRAINT DF_AppUsers_Failed DEFAULT (0),
        LockedUntil        datetime2(0)   NULL,
        LastSignIn         datetime2(0)   NULL,
        CreatedOn          datetime2(0)   NOT NULL CONSTRAINT DF_AppUsers_CreatedOn DEFAULT (SYSDATETIME()),
        CreatedBy          nvarchar(128)  NOT NULL CONSTRAINT DF_AppUsers_CreatedBy DEFAULT (ORIGINAL_LOGIN())
    );
GO

GO


/* ==================== 2 of 12: attendance_install.sql ==================== */
PRINT N'2 of 12: attendance_install';
GO
/* Student attendance: new tables, monthly summary view, and copy of existing trial data.
   Run as a database owner. Safe to re-run.
   Old tables tblAttendanceMaster / tblAttendanceChild are NOT dropped here
   (Access still uses them) - see attendance_remove_old.sql. */

-- 1. Status codes
IF OBJECT_ID('dbo.tblAttendanceStatus') IS NULL
BEGIN
    CREATE TABLE dbo.tblAttendanceStatus (
        StatusCode      char(1)      NOT NULL CONSTRAINT PK_tblAttendanceStatus PRIMARY KEY,
        StatusName      nvarchar(30) NOT NULL,
        CountsAsPresent bit          NOT NULL,
        SortOrder       tinyint      NOT NULL
    );
    INSERT dbo.tblAttendanceStatus VALUES
        ('P', N'Present', 1, 1),
        ('A', N'Absent',  0, 2),
        ('L', N'Leave',   0, 3),
        ('T', N'Late',    1, 4);
END
GO

-- 2. School calendar: holidays and closed days
IF OBJECT_ID('dbo.tblSchoolCalendar') IS NULL
    CREATE TABLE dbo.tblSchoolCalendar (
        CalDate   date          NOT NULL CONSTRAINT PK_tblSchoolCalendar PRIMARY KEY,
        IsHoliday bit           NOT NULL CONSTRAINT DF_tblSchoolCalendar_IsHoliday DEFAULT (1),
        Reason    nvarchar(200) NULL
    );
GO

-- 3. Register: one per class, section and date
IF OBJECT_ID('dbo.tblAttendanceRegister') IS NULL
    CREATE TABLE dbo.tblAttendanceRegister (
        RegisterID int           NOT NULL IDENTITY CONSTRAINT PK_tblAttendanceRegister PRIMARY KEY,
        AttDate    date          NOT NULL,
        ClassID    int           NOT NULL CONSTRAINT FK_AttReg_Classes  REFERENCES dbo.Classes (ClassID),
        SectionID  int           NOT NULL CONSTRAINT FK_AttReg_Sections REFERENCES dbo.Sections (SectionID),
        TakenBy    nvarchar(128) NOT NULL CONSTRAINT DF_AttReg_TakenBy DEFAULT (ORIGINAL_LOGIN()),
        TakenOn    datetime2(0)  NOT NULL CONSTRAINT DF_AttReg_TakenOn DEFAULT (SYSDATETIME()),
        IsLocked   bit           NOT NULL CONSTRAINT DF_AttReg_IsLocked DEFAULT (0),
        Remarks    nvarchar(500) NULL,
        CONSTRAINT UQ_AttReg_Date_Class_Section UNIQUE (AttDate, ClassID, SectionID)
    );
GO

-- 4. One row per student per register
IF OBJECT_ID('dbo.tblStudentAttendance') IS NULL
BEGIN
    CREATE TABLE dbo.tblStudentAttendance (
        AttendanceID int           NOT NULL IDENTITY CONSTRAINT PK_tblStudentAttendance PRIMARY KEY,
        RegisterID   int           NOT NULL CONSTRAINT FK_StuAtt_Register REFERENCES dbo.tblAttendanceRegister (RegisterID) ON DELETE CASCADE,
        CC           int           NOT NULL CONSTRAINT FK_StuAtt_Students REFERENCES dbo.Students (CC),
        StatusCode   char(1)       NOT NULL CONSTRAINT FK_StuAtt_Status   REFERENCES dbo.tblAttendanceStatus (StatusCode),
        Remarks      nvarchar(500) NULL,
        CONSTRAINT UQ_StuAtt_Register_CC UNIQUE (RegisterID, CC)
    );
    CREATE INDEX IX_StuAtt_CC ON dbo.tblStudentAttendance (CC) INCLUDE (StatusCode);
END
GO

-- 5. Monthly summary per student
CREATE OR ALTER VIEW dbo.vwStudentAttendanceMonthly
AS
SELECT  sa.CC,
        YEAR(r.AttDate)  AS AttYear,
        MONTH(r.AttDate) AS AttMonth,
        COUNT(*)                                          AS DaysMarked,
        SUM(CASE WHEN sa.StatusCode = 'P' THEN 1 ELSE 0 END) AS Present,
        SUM(CASE WHEN sa.StatusCode = 'A' THEN 1 ELSE 0 END) AS Absent,
        SUM(CASE WHEN sa.StatusCode = 'L' THEN 1 ELSE 0 END) AS Leave,
        SUM(CASE WHEN sa.StatusCode = 'T' THEN 1 ELSE 0 END) AS Late,
        CAST(100.0 * SUM(CASE WHEN s.CountsAsPresent = 1 THEN 1 ELSE 0 END) / COUNT(*) AS decimal(5,1)) AS AttendancePct
FROM    dbo.tblStudentAttendance sa
JOIN    dbo.tblAttendanceRegister r ON r.RegisterID = sa.RegisterID
JOIN    dbo.tblAttendanceStatus  s ON s.StatusCode = sa.StatusCode
GROUP BY sa.CC, YEAR(r.AttDate), MONTH(r.AttDate);
GO

-- 6. Copy existing trial data (5 Dec 2025, 72 students) from the old tables
IF NOT EXISTS (SELECT 1 FROM dbo.tblAttendanceRegister)
BEGIN
    INSERT dbo.tblSchoolCalendar (CalDate, IsHoliday, Reason)
    SELECT DISTINCT m.Attd_Date, 1, m.Remarks
    FROM dbo.tblAttendanceMaster m
    WHERE m.IsHoliday = 1 AND m.Attd_Date IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.tblSchoolCalendar c WHERE c.CalDate = m.Attd_Date);

    INSERT dbo.tblAttendanceRegister (AttDate, ClassID, SectionID, TakenBy, Remarks)
    SELECT DISTINCT m.Attd_Date, c.fk_ClassID, c.fk_SectionID, N'(imported from Access)', NULL
    FROM dbo.tblAttendanceChild c
    JOIN dbo.tblAttendanceMaster m ON m.Attd_ID = c.fk_Attd_Master_ID
    WHERE m.Attd_Date IS NOT NULL AND c.fk_ClassID IS NOT NULL AND c.fk_SectionID IS NOT NULL;

    INSERT dbo.tblStudentAttendance (RegisterID, CC, StatusCode, Remarks)
    SELECT r.RegisterID, c.FK_cc, UPPER(LEFT(c.Attendance, 1)), c.Remarks
    FROM dbo.tblAttendanceChild c
    JOIN dbo.tblAttendanceMaster m   ON m.Attd_ID = c.fk_Attd_Master_ID
    JOIN dbo.tblAttendanceRegister r ON r.AttDate = m.Attd_Date AND r.ClassID = c.fk_ClassID AND r.SectionID = c.fk_SectionID
    WHERE c.FK_cc IN (SELECT CC FROM dbo.Students)
      AND UPPER(LEFT(c.Attendance, 1)) IN (SELECT StatusCode FROM dbo.tblAttendanceStatus);
END
GO

GO


/* ==================== 3 of 12: attendance_roles_install.sql ==================== */
PRINT N'3 of 12: attendance_roles_install';
GO
/* Attendance, part 2: Coordinator and Teacher in charge roles, what each user is assigned to, and staff attendance.
   Run as a database owner after app_users_install.sql and attendance_install.sql. Safe to re-run.
   Coordinators mark today's attendance of their departments, a teacher in charge their own class and section; only an Admin changes earlier days.
   They connect with their own Windows account like everyone else: add it as in windows_logins_template.sql. */

-- 1. The two new roles
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AppUsers_Role'
           AND definition NOT LIKE '%TeacherInCharge%')
    ALTER TABLE dbo.AppUsers DROP CONSTRAINT CK_AppUsers_Role;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_AppUsers_Role')
    ALTER TABLE dbo.AppUsers ADD CONSTRAINT CK_AppUsers_Role
        CHECK (RoleName IN ('Admin','FeeClerk','Admissions','ReadOnly','Coordinator','TeacherInCharge'));
GO

-- 2. What a user looks after: a department (coordinator) or a class and section (teacher in charge)
IF OBJECT_ID('dbo.tblUserScope') IS NULL
    CREATE TABLE dbo.tblUserScope (
        ScopeID      int          NOT NULL IDENTITY CONSTRAINT PK_tblUserScope PRIMARY KEY,
        UserName     nvarchar(50) NOT NULL CONSTRAINT FK_UserScope_AppUsers REFERENCES dbo.AppUsers (UserName) ON DELETE CASCADE,
        DepartmentID int          NULL CONSTRAINT FK_UserScope_Departments REFERENCES dbo.Departments (DeptNo),
        ClassID      int          NULL CONSTRAINT FK_UserScope_Classes REFERENCES dbo.Classes (ClassID),
        SectionID    int          NULL CONSTRAINT FK_UserScope_Sections REFERENCES dbo.Sections (SectionID),
        CONSTRAINT CK_UserScope_What CHECK (DepartmentID IS NOT NULL OR (ClassID IS NOT NULL AND SectionID IS NOT NULL))
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UserScope_User')
    CREATE INDEX IX_UserScope_User ON dbo.tblUserScope (UserName);
GO

-- 3. Staff attendance: one row per staff member per date, with optional time in and out
IF OBJECT_ID('dbo.tblStaffAttendance') IS NULL
BEGIN
    CREATE TABLE dbo.tblStaffAttendance (
        StaffAttendanceID int           NOT NULL IDENTITY CONSTRAINT PK_tblStaffAttendance PRIMARY KEY,
        AttDate           date          NOT NULL,
        TeachersID        int           NOT NULL CONSTRAINT FK_StaffAtt_Staff  REFERENCES dbo.Staff (TeachersID),
        StatusCode        char(1)       NOT NULL CONSTRAINT FK_StaffAtt_Status REFERENCES dbo.tblAttendanceStatus (StatusCode),
        TimeIn            time(0)       NULL,
        TimeOut           time(0)       NULL,
        Remarks           nvarchar(500) NULL,
        TakenBy           nvarchar(128) NOT NULL CONSTRAINT DF_StaffAtt_TakenBy DEFAULT (ORIGINAL_LOGIN()),
        TakenOn           datetime2(0)  NOT NULL CONSTRAINT DF_StaffAtt_TakenOn DEFAULT (SYSDATETIME()),
        CONSTRAINT UQ_StaffAtt_Date_Staff UNIQUE (AttDate, TeachersID)
    );
    CREATE INDEX IX_StaffAtt_Staff ON dbo.tblStaffAttendance (TeachersID) INCLUDE (StatusCode);
END
GO

GO


/* ==================== 4 of 12: audit_install.sql ==================== */
PRINT N'4 of 12: audit_install';
GO
/* Audit trail for ArqamBase_be
   Logs every insert, update and delete on: Feerecipt, FeeDefinitation, Students, Family, Staff, Users, tblAttendanceRegister, tblStudentAttendance
   Run as a database owner on the database selected in SSMS. Safe to re-run. */
IF SCHEMA_ID('audit') IS NULL EXEC('CREATE SCHEMA audit AUTHORIZATION dbo');
GO
IF OBJECT_ID('audit.AuditLogSeq') IS NULL
    CREATE SEQUENCE audit.AuditLogSeq AS bigint START WITH 1;
GO
-- AuditID uses a sequence, not IDENTITY, so @@IDENTITY in the Access front end is not affected.
IF OBJECT_ID('audit.AuditLog') IS NULL
BEGIN
    CREATE TABLE audit.AuditLog (
        AuditID    bigint        NOT NULL CONSTRAINT DF_AuditLog_ID DEFAULT (NEXT VALUE FOR audit.AuditLogSeq)
                                          CONSTRAINT PK_AuditLog PRIMARY KEY,
        ChangedAt  datetime2(3)  NOT NULL,
        TableName  sysname       NOT NULL,
        KeyValue   int           NOT NULL,   -- primary key of the changed row
        Action     char(1)       NOT NULL,   -- I = insert, U = update, D = delete
        ColumnName sysname       NULL,       -- set for updates; NULL for insert/delete
        OldValue   nvarchar(max) NULL,       -- update: old value; delete: whole row as JSON
        NewValue   nvarchar(max) NULL,       -- update: new value; insert: whole row as JSON
        SqlLogin     nvarchar(128) NOT NULL, -- SQL Server login that made the change
        WindowsUser  nvarchar(128) NULL,     -- Windows login (from a Windows-auth login, or sent by the app)
        ComputerName nvarchar(128) NULL,     -- computer the change came from
        AppName      nvarchar(128) NULL
    );
    CREATE INDEX IX_AuditLog_Table_Key ON audit.AuditLog (TableName, KeyValue, ChangedAt);
    CREATE INDEX IX_AuditLog_ChangedAt ON audit.AuditLog (ChangedAt);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_Feerecipt ON dbo.[Feerecipt]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Feerecipt', i.[Sr#], 'I', NULL, NULL,
           (SELECT i.[Sr#] AS [Sr#],
                    i.[IssueDate] AS [IssueDate],
                    i.[ReDate] AS [ReDate],
                    i.[Period] AS [Period],
                    i.[CC] AS [CC],
                    i.[TuitionFee] AS [TuitionFee],
                    i.[ExamsFee] AS [ExamsFee],
                    i.[Mis_fee] AS [Mis_fee],
                    i.[AdmissionFee] AS [AdmissionFee],
                    i.[ProspectusFee] AS [ProspectusFee],
                    i.[Fine] AS [Fine],
                    i.[RecipNo] AS [RecipNo],
                    i.[Other] AS [Other],
                    i.[ReceivedAmount] AS [ReceivedAmount],
                    i.[Paid] AS [Paid],
                    i.[DateEdited] AS [DateEdited],
                    i.[DRCR] AS [DRCR],
                    i.[Description] AS [Description],
                    i.[DueDate] AS [DueDate],
                    i.[Remarks] AS [Remarks],
                    i.[CollectionType] AS [CollectionType],
                    i.[isDeleted] AS [isDeleted],
                    i.[CreatedBy] AS [CreatedBy],
                    i.[CreatedOn] AS [CreatedOn],
                    i.[DeletedBy] AS [DeletedBy],
                    i.[DeletedOn] AS [DeletedOn]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Sr#] = i.[Sr#]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Feerecipt', d.[Sr#], 'D', NULL,
           (SELECT d.[Sr#] AS [Sr#],
                    d.[IssueDate] AS [IssueDate],
                    d.[ReDate] AS [ReDate],
                    d.[Period] AS [Period],
                    d.[CC] AS [CC],
                    d.[TuitionFee] AS [TuitionFee],
                    d.[ExamsFee] AS [ExamsFee],
                    d.[Mis_fee] AS [Mis_fee],
                    d.[AdmissionFee] AS [AdmissionFee],
                    d.[ProspectusFee] AS [ProspectusFee],
                    d.[Fine] AS [Fine],
                    d.[RecipNo] AS [RecipNo],
                    d.[Other] AS [Other],
                    d.[ReceivedAmount] AS [ReceivedAmount],
                    d.[Paid] AS [Paid],
                    d.[DateEdited] AS [DateEdited],
                    d.[DRCR] AS [DRCR],
                    d.[Description] AS [Description],
                    d.[DueDate] AS [DueDate],
                    d.[Remarks] AS [Remarks],
                    d.[CollectionType] AS [CollectionType],
                    d.[isDeleted] AS [isDeleted],
                    d.[CreatedBy] AS [CreatedBy],
                    d.[CreatedOn] AS [CreatedOn],
                    d.[DeletedBy] AS [DeletedBy],
                    d.[DeletedOn] AS [DeletedOn]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[Sr#] = d.[Sr#]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Feerecipt', i.[Sr#], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[Sr#] = i.[Sr#]
    CROSS APPLY (VALUES
            (N'Sr#', CONVERT(nvarchar(max), d.[Sr#]), CONVERT(nvarchar(max), i.[Sr#])),
            (N'IssueDate', CONVERT(nvarchar(max), d.[IssueDate], 126), CONVERT(nvarchar(max), i.[IssueDate], 126)),
            (N'ReDate', CONVERT(nvarchar(max), d.[ReDate], 126), CONVERT(nvarchar(max), i.[ReDate], 126)),
            (N'Period', CONVERT(nvarchar(max), d.[Period]), CONVERT(nvarchar(max), i.[Period])),
            (N'CC', CONVERT(nvarchar(max), d.[CC]), CONVERT(nvarchar(max), i.[CC])),
            (N'TuitionFee', CONVERT(nvarchar(max), d.[TuitionFee]), CONVERT(nvarchar(max), i.[TuitionFee])),
            (N'ExamsFee', CONVERT(nvarchar(max), d.[ExamsFee]), CONVERT(nvarchar(max), i.[ExamsFee])),
            (N'Mis_fee', CONVERT(nvarchar(max), d.[Mis_fee]), CONVERT(nvarchar(max), i.[Mis_fee])),
            (N'AdmissionFee', CONVERT(nvarchar(max), d.[AdmissionFee]), CONVERT(nvarchar(max), i.[AdmissionFee])),
            (N'ProspectusFee', CONVERT(nvarchar(max), d.[ProspectusFee]), CONVERT(nvarchar(max), i.[ProspectusFee])),
            (N'Fine', CONVERT(nvarchar(max), d.[Fine]), CONVERT(nvarchar(max), i.[Fine])),
            (N'RecipNo', CONVERT(nvarchar(max), d.[RecipNo]), CONVERT(nvarchar(max), i.[RecipNo])),
            (N'Other', CONVERT(nvarchar(max), d.[Other]), CONVERT(nvarchar(max), i.[Other])),
            (N'ReceivedAmount', CONVERT(nvarchar(max), d.[ReceivedAmount]), CONVERT(nvarchar(max), i.[ReceivedAmount])),
            (N'Paid', CONVERT(nvarchar(max), d.[Paid]), CONVERT(nvarchar(max), i.[Paid])),
            (N'DateEdited', CONVERT(nvarchar(max), d.[DateEdited], 126), CONVERT(nvarchar(max), i.[DateEdited], 126)),
            (N'DRCR', CONVERT(nvarchar(max), d.[DRCR]), CONVERT(nvarchar(max), i.[DRCR])),
            (N'Description', CONVERT(nvarchar(max), d.[Description]), CONVERT(nvarchar(max), i.[Description])),
            (N'DueDate', CONVERT(nvarchar(max), d.[DueDate], 126), CONVERT(nvarchar(max), i.[DueDate], 126)),
            (N'Remarks', CONVERT(nvarchar(max), d.[Remarks]), CONVERT(nvarchar(max), i.[Remarks])),
            (N'CollectionType', CONVERT(nvarchar(max), d.[CollectionType]), CONVERT(nvarchar(max), i.[CollectionType])),
            (N'isDeleted', CONVERT(nvarchar(max), d.[isDeleted]), CONVERT(nvarchar(max), i.[isDeleted])),
            (N'CreatedBy', CONVERT(nvarchar(max), d.[CreatedBy]), CONVERT(nvarchar(max), i.[CreatedBy])),
            (N'CreatedOn', CONVERT(nvarchar(max), d.[CreatedOn], 126), CONVERT(nvarchar(max), i.[CreatedOn], 126)),
            (N'DeletedBy', CONVERT(nvarchar(max), d.[DeletedBy]), CONVERT(nvarchar(max), i.[DeletedBy])),
            (N'DeletedOn', CONVERT(nvarchar(max), d.[DeletedOn], 126), CONVERT(nvarchar(max), i.[DeletedOn], 126))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_FeeDefinitation ON dbo.[FeeDefinitation]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'FeeDefinitation', i.[FeeDefId], 'I', NULL, NULL,
           (SELECT i.[FeeDefId] AS [FeeDefId],
                    i.[DS#] AS [DS#],
                    i.[TuitionFee] AS [TuitionFee],
                    i.[ExamsFee] AS [ExamsFee],
                    i.[MiscFee] AS [MiscFee],
                    i.[LabFee] AS [LabFee],
                    i.[ParkingFee] AS [ParkingFee],
                    i.[Other] AS [Other],
                    i.[AdmissionFee] AS [AdmissionFee],
                    i.[FK_CC] AS [FK_CC],
                    i.[CreatedDate] AS [CreatedDate],
                    i.[RevisionDate] AS [RevisionDate],
                    i.[RevisionNo] AS [RevisionNo],
                    i.[isDeleted] AS [isDeleted],
                    i.[CreatedBy] AS [CreatedBy],
                    i.[CreatedOn] AS [CreatedOn],
                    i.[DeletedBy] AS [DeletedBy],
                    i.[DeletedOn] AS [DeletedOn]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[FeeDefId] = i.[FeeDefId]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'FeeDefinitation', d.[FeeDefId], 'D', NULL,
           (SELECT d.[FeeDefId] AS [FeeDefId],
                    d.[DS#] AS [DS#],
                    d.[TuitionFee] AS [TuitionFee],
                    d.[ExamsFee] AS [ExamsFee],
                    d.[MiscFee] AS [MiscFee],
                    d.[LabFee] AS [LabFee],
                    d.[ParkingFee] AS [ParkingFee],
                    d.[Other] AS [Other],
                    d.[AdmissionFee] AS [AdmissionFee],
                    d.[FK_CC] AS [FK_CC],
                    d.[CreatedDate] AS [CreatedDate],
                    d.[RevisionDate] AS [RevisionDate],
                    d.[RevisionNo] AS [RevisionNo],
                    d.[isDeleted] AS [isDeleted],
                    d.[CreatedBy] AS [CreatedBy],
                    d.[CreatedOn] AS [CreatedOn],
                    d.[DeletedBy] AS [DeletedBy],
                    d.[DeletedOn] AS [DeletedOn]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[FeeDefId] = d.[FeeDefId]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'FeeDefinitation', i.[FeeDefId], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[FeeDefId] = i.[FeeDefId]
    CROSS APPLY (VALUES
            (N'FeeDefId', CONVERT(nvarchar(max), d.[FeeDefId]), CONVERT(nvarchar(max), i.[FeeDefId])),
            (N'DS#', CONVERT(nvarchar(max), d.[DS#]), CONVERT(nvarchar(max), i.[DS#])),
            (N'TuitionFee', CONVERT(nvarchar(max), d.[TuitionFee]), CONVERT(nvarchar(max), i.[TuitionFee])),
            (N'ExamsFee', CONVERT(nvarchar(max), d.[ExamsFee]), CONVERT(nvarchar(max), i.[ExamsFee])),
            (N'MiscFee', CONVERT(nvarchar(max), d.[MiscFee]), CONVERT(nvarchar(max), i.[MiscFee])),
            (N'LabFee', CONVERT(nvarchar(max), d.[LabFee]), CONVERT(nvarchar(max), i.[LabFee])),
            (N'ParkingFee', CONVERT(nvarchar(max), d.[ParkingFee]), CONVERT(nvarchar(max), i.[ParkingFee])),
            (N'Other', CONVERT(nvarchar(max), d.[Other]), CONVERT(nvarchar(max), i.[Other])),
            (N'AdmissionFee', CONVERT(nvarchar(max), d.[AdmissionFee]), CONVERT(nvarchar(max), i.[AdmissionFee])),
            (N'FK_CC', CONVERT(nvarchar(max), d.[FK_CC]), CONVERT(nvarchar(max), i.[FK_CC])),
            (N'CreatedDate', CONVERT(nvarchar(max), d.[CreatedDate], 126), CONVERT(nvarchar(max), i.[CreatedDate], 126)),
            (N'RevisionDate', CONVERT(nvarchar(max), d.[RevisionDate], 126), CONVERT(nvarchar(max), i.[RevisionDate], 126)),
            (N'RevisionNo', CONVERT(nvarchar(max), d.[RevisionNo]), CONVERT(nvarchar(max), i.[RevisionNo])),
            (N'isDeleted', CONVERT(nvarchar(max), d.[isDeleted]), CONVERT(nvarchar(max), i.[isDeleted])),
            (N'CreatedBy', CONVERT(nvarchar(max), d.[CreatedBy]), CONVERT(nvarchar(max), i.[CreatedBy])),
            (N'CreatedOn', CONVERT(nvarchar(max), d.[CreatedOn], 126), CONVERT(nvarchar(max), i.[CreatedOn], 126)),
            (N'DeletedBy', CONVERT(nvarchar(max), d.[DeletedBy]), CONVERT(nvarchar(max), i.[DeletedBy])),
            (N'DeletedOn', CONVERT(nvarchar(max), d.[DeletedOn], 126), CONVERT(nvarchar(max), i.[DeletedOn], 126))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_Students ON dbo.[Students]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Students', i.[CC], 'I', NULL, NULL,
           (SELECT i.[CC] AS [CC],
                    i.[DS#] AS [DS#],
                    i.[FamilyID] AS [FamilyID],
                    i.[AdmissionDate] AS [AdmissionDate],
                    i.[Name] AS [Name],
                    i.[BirthDate] AS [BirthDate],
                    i.[BirthPlace] AS [BirthPlace],
                    i.[Sex] AS [Sex],
                    i.[AdmissionClass] AS [AdmissionClass],
                    i.[CurrentClass] AS [CurrentClass],
                    i.[CurrentSection] AS [CurrentSection],
                    i.[Flag] AS [Flag],
                    i.[Status] AS [Status],
                    i.[Remarks] AS [Remarks],
                    i.[Snap] AS [Snap],
                    i.[FK_ADMTestID] AS [FK_ADMTestID],
                    i.[SLDate] AS [SLDate],
                    i.[Left] AS [Left],
                    i.[PromotionDate] AS [PromotionDate],
                    i.[PreviousClass] AS [PreviousClass],
                    CASE WHEN i.[Picture] IS NULL THEN NULL ELSE N'***' END AS [Picture],
                    i.[Fee] AS [Fee],
                    i.[Email] AS [Email],
                    i.[WhatsApp] AS [WhatsApp],
                    i.[isDeleted] AS [isDeleted],
                    i.[CreatedBy] AS [CreatedBy],
                    i.[CreatedOn] AS [CreatedOn],
                    i.[DeletedBy] AS [DeletedBy],
                    i.[DeletedOn] AS [DeletedOn],
                    i.[ReasonLeftSchool] AS [ReasonLeftSchool],
                    i.[fk_BloodGroup] AS [fk_BloodGroup],
                    i.[FormB] AS [FormB],
                    i.[StudentNameUrdu] AS [StudentNameUrdu]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[CC] = i.[CC]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Students', d.[CC], 'D', NULL,
           (SELECT d.[CC] AS [CC],
                    d.[DS#] AS [DS#],
                    d.[FamilyID] AS [FamilyID],
                    d.[AdmissionDate] AS [AdmissionDate],
                    d.[Name] AS [Name],
                    d.[BirthDate] AS [BirthDate],
                    d.[BirthPlace] AS [BirthPlace],
                    d.[Sex] AS [Sex],
                    d.[AdmissionClass] AS [AdmissionClass],
                    d.[CurrentClass] AS [CurrentClass],
                    d.[CurrentSection] AS [CurrentSection],
                    d.[Flag] AS [Flag],
                    d.[Status] AS [Status],
                    d.[Remarks] AS [Remarks],
                    d.[Snap] AS [Snap],
                    d.[FK_ADMTestID] AS [FK_ADMTestID],
                    d.[SLDate] AS [SLDate],
                    d.[Left] AS [Left],
                    d.[PromotionDate] AS [PromotionDate],
                    d.[PreviousClass] AS [PreviousClass],
                    CASE WHEN d.[Picture] IS NULL THEN NULL ELSE N'***' END AS [Picture],
                    d.[Fee] AS [Fee],
                    d.[Email] AS [Email],
                    d.[WhatsApp] AS [WhatsApp],
                    d.[isDeleted] AS [isDeleted],
                    d.[CreatedBy] AS [CreatedBy],
                    d.[CreatedOn] AS [CreatedOn],
                    d.[DeletedBy] AS [DeletedBy],
                    d.[DeletedOn] AS [DeletedOn],
                    d.[ReasonLeftSchool] AS [ReasonLeftSchool],
                    d.[fk_BloodGroup] AS [fk_BloodGroup],
                    d.[FormB] AS [FormB],
                    d.[StudentNameUrdu] AS [StudentNameUrdu]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[CC] = d.[CC]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Students', i.[CC], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[CC] = i.[CC]
    CROSS APPLY (VALUES
            (N'CC', CONVERT(nvarchar(max), d.[CC]), CONVERT(nvarchar(max), i.[CC])),
            (N'DS#', CONVERT(nvarchar(max), d.[DS#]), CONVERT(nvarchar(max), i.[DS#])),
            (N'FamilyID', CONVERT(nvarchar(max), d.[FamilyID]), CONVERT(nvarchar(max), i.[FamilyID])),
            (N'AdmissionDate', CONVERT(nvarchar(max), d.[AdmissionDate], 126), CONVERT(nvarchar(max), i.[AdmissionDate], 126)),
            (N'Name', CONVERT(nvarchar(max), d.[Name]), CONVERT(nvarchar(max), i.[Name])),
            (N'BirthDate', CONVERT(nvarchar(max), d.[BirthDate], 126), CONVERT(nvarchar(max), i.[BirthDate], 126)),
            (N'BirthPlace', CONVERT(nvarchar(max), d.[BirthPlace]), CONVERT(nvarchar(max), i.[BirthPlace])),
            (N'Sex', CONVERT(nvarchar(max), d.[Sex]), CONVERT(nvarchar(max), i.[Sex])),
            (N'AdmissionClass', CONVERT(nvarchar(max), d.[AdmissionClass]), CONVERT(nvarchar(max), i.[AdmissionClass])),
            (N'CurrentClass', CONVERT(nvarchar(max), d.[CurrentClass]), CONVERT(nvarchar(max), i.[CurrentClass])),
            (N'CurrentSection', CONVERT(nvarchar(max), d.[CurrentSection]), CONVERT(nvarchar(max), i.[CurrentSection])),
            (N'Flag', CONVERT(nvarchar(max), d.[Flag]), CONVERT(nvarchar(max), i.[Flag])),
            (N'Status', CONVERT(nvarchar(max), d.[Status]), CONVERT(nvarchar(max), i.[Status])),
            (N'Remarks', CONVERT(nvarchar(max), d.[Remarks]), CONVERT(nvarchar(max), i.[Remarks])),
            (N'Snap', CONVERT(nvarchar(max), d.[Snap]), CONVERT(nvarchar(max), i.[Snap])),
            (N'FK_ADMTestID', CONVERT(nvarchar(max), d.[FK_ADMTestID], 3), CONVERT(nvarchar(max), i.[FK_ADMTestID], 3)),
            (N'SLDate', CONVERT(nvarchar(max), d.[SLDate], 126), CONVERT(nvarchar(max), i.[SLDate], 126)),
            (N'Left', CONVERT(nvarchar(max), d.[Left]), CONVERT(nvarchar(max), i.[Left])),
            (N'PromotionDate', CONVERT(nvarchar(max), d.[PromotionDate]), CONVERT(nvarchar(max), i.[PromotionDate])),
            (N'PreviousClass', CONVERT(nvarchar(max), d.[PreviousClass], 3), CONVERT(nvarchar(max), i.[PreviousClass], 3)),
            (N'Picture', CASE WHEN d.[Picture] IS NULL THEN NULL ELSE N'***' END, CASE WHEN i.[Picture] IS NULL THEN NULL WHEN d.[Picture] COLLATE Latin1_General_BIN2 = i.[Picture] COLLATE Latin1_General_BIN2 THEN N'***' ELSE N'*** (changed)' END),
            (N'Fee', CONVERT(nvarchar(max), d.[Fee], 2), CONVERT(nvarchar(max), i.[Fee], 2)),
            (N'Email', CONVERT(nvarchar(max), d.[Email]), CONVERT(nvarchar(max), i.[Email])),
            (N'WhatsApp', CONVERT(nvarchar(max), d.[WhatsApp]), CONVERT(nvarchar(max), i.[WhatsApp])),
            (N'isDeleted', CONVERT(nvarchar(max), d.[isDeleted]), CONVERT(nvarchar(max), i.[isDeleted])),
            (N'CreatedBy', CONVERT(nvarchar(max), d.[CreatedBy], 3), CONVERT(nvarchar(max), i.[CreatedBy], 3)),
            (N'CreatedOn', CONVERT(nvarchar(max), d.[CreatedOn], 126), CONVERT(nvarchar(max), i.[CreatedOn], 126)),
            (N'DeletedBy', CONVERT(nvarchar(max), d.[DeletedBy], 3), CONVERT(nvarchar(max), i.[DeletedBy], 3)),
            (N'DeletedOn', CONVERT(nvarchar(max), d.[DeletedOn], 126), CONVERT(nvarchar(max), i.[DeletedOn], 126)),
            (N'ReasonLeftSchool', CONVERT(nvarchar(max), d.[ReasonLeftSchool]), CONVERT(nvarchar(max), i.[ReasonLeftSchool])),
            (N'fk_BloodGroup', CONVERT(nvarchar(max), d.[fk_BloodGroup], 3), CONVERT(nvarchar(max), i.[fk_BloodGroup], 3)),
            (N'FormB', CONVERT(nvarchar(max), d.[FormB]), CONVERT(nvarchar(max), i.[FormB])),
            (N'StudentNameUrdu', CONVERT(nvarchar(max), d.[StudentNameUrdu]), CONVERT(nvarchar(max), i.[StudentNameUrdu]))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_Family ON dbo.[Family]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Family', i.[FormID], 'I', NULL, NULL,
           (SELECT i.[FormID] AS [FormID],
                    i.[FatherName] AS [FatherName],
                    i.[Caste] AS [Caste],
                    i.[NIC#] AS [NIC#],
                    i.[FathersEdu] AS [FathersEdu],
                    i.[Profession] AS [Profession],
                    i.[BusinessName] AS [BusinessName],
                    i.[CurrentAdress] AS [CurrentAdress],
                    i.[PermanentAddress] AS [PermanentAddress],
                    i.[PhHome] AS [PhHome],
                    i.[PhOffice] AS [PhOffice],
                    i.[Mobile] AS [Mobile],
                    i.[MotherName] AS [MotherName],
                    i.[Mcell] AS [Mcell],
                    i.[EmerContactName] AS [EmerContactName],
                    i.[EmerNo] AS [EmerNo],
                    i.[Nationality] AS [Nationality],
                    i.[MotherEdu] AS [MotherEdu],
                    i.[Flag] AS [Flag],
                    i.[isDeleted] AS [isDeleted],
                    i.[CreatedBy] AS [CreatedBy],
                    i.[CreatedOn] AS [CreatedOn],
                    i.[DeletedBy] AS [DeletedBy],
                    i.[DeletedOn] AS [DeletedOn],
                    i.[FamilyNotes] AS [FamilyNotes],
                    i.[SpecialInstructions] AS [SpecialInstructions],
                    i.[LastUpdatedBy] AS [LastUpdatedBy],
                    i.[LastUpdatedat] AS [LastUpdatedat],
                    i.[FatherNameUrdu] AS [FatherNameUrdu]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[FormID] = i.[FormID]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Family', d.[FormID], 'D', NULL,
           (SELECT d.[FormID] AS [FormID],
                    d.[FatherName] AS [FatherName],
                    d.[Caste] AS [Caste],
                    d.[NIC#] AS [NIC#],
                    d.[FathersEdu] AS [FathersEdu],
                    d.[Profession] AS [Profession],
                    d.[BusinessName] AS [BusinessName],
                    d.[CurrentAdress] AS [CurrentAdress],
                    d.[PermanentAddress] AS [PermanentAddress],
                    d.[PhHome] AS [PhHome],
                    d.[PhOffice] AS [PhOffice],
                    d.[Mobile] AS [Mobile],
                    d.[MotherName] AS [MotherName],
                    d.[Mcell] AS [Mcell],
                    d.[EmerContactName] AS [EmerContactName],
                    d.[EmerNo] AS [EmerNo],
                    d.[Nationality] AS [Nationality],
                    d.[MotherEdu] AS [MotherEdu],
                    d.[Flag] AS [Flag],
                    d.[isDeleted] AS [isDeleted],
                    d.[CreatedBy] AS [CreatedBy],
                    d.[CreatedOn] AS [CreatedOn],
                    d.[DeletedBy] AS [DeletedBy],
                    d.[DeletedOn] AS [DeletedOn],
                    d.[FamilyNotes] AS [FamilyNotes],
                    d.[SpecialInstructions] AS [SpecialInstructions],
                    d.[LastUpdatedBy] AS [LastUpdatedBy],
                    d.[LastUpdatedat] AS [LastUpdatedat],
                    d.[FatherNameUrdu] AS [FatherNameUrdu]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[FormID] = d.[FormID]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Family', i.[FormID], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[FormID] = i.[FormID]
    CROSS APPLY (VALUES
            (N'FormID', CONVERT(nvarchar(max), d.[FormID]), CONVERT(nvarchar(max), i.[FormID])),
            (N'FatherName', CONVERT(nvarchar(max), d.[FatherName]), CONVERT(nvarchar(max), i.[FatherName])),
            (N'Caste', CONVERT(nvarchar(max), d.[Caste]), CONVERT(nvarchar(max), i.[Caste])),
            (N'NIC#', CONVERT(nvarchar(max), d.[NIC#]), CONVERT(nvarchar(max), i.[NIC#])),
            (N'FathersEdu', CONVERT(nvarchar(max), d.[FathersEdu]), CONVERT(nvarchar(max), i.[FathersEdu])),
            (N'Profession', CONVERT(nvarchar(max), d.[Profession]), CONVERT(nvarchar(max), i.[Profession])),
            (N'BusinessName', CONVERT(nvarchar(max), d.[BusinessName]), CONVERT(nvarchar(max), i.[BusinessName])),
            (N'CurrentAdress', CONVERT(nvarchar(max), d.[CurrentAdress]), CONVERT(nvarchar(max), i.[CurrentAdress])),
            (N'PermanentAddress', CONVERT(nvarchar(max), d.[PermanentAddress]), CONVERT(nvarchar(max), i.[PermanentAddress])),
            (N'PhHome', CONVERT(nvarchar(max), d.[PhHome]), CONVERT(nvarchar(max), i.[PhHome])),
            (N'PhOffice', CONVERT(nvarchar(max), d.[PhOffice]), CONVERT(nvarchar(max), i.[PhOffice])),
            (N'Mobile', CONVERT(nvarchar(max), d.[Mobile]), CONVERT(nvarchar(max), i.[Mobile])),
            (N'MotherName', CONVERT(nvarchar(max), d.[MotherName]), CONVERT(nvarchar(max), i.[MotherName])),
            (N'Mcell', CONVERT(nvarchar(max), d.[Mcell]), CONVERT(nvarchar(max), i.[Mcell])),
            (N'EmerContactName', CONVERT(nvarchar(max), d.[EmerContactName]), CONVERT(nvarchar(max), i.[EmerContactName])),
            (N'EmerNo', CONVERT(nvarchar(max), d.[EmerNo]), CONVERT(nvarchar(max), i.[EmerNo])),
            (N'Nationality', CONVERT(nvarchar(max), d.[Nationality]), CONVERT(nvarchar(max), i.[Nationality])),
            (N'MotherEdu', CONVERT(nvarchar(max), d.[MotherEdu]), CONVERT(nvarchar(max), i.[MotherEdu])),
            (N'Flag', CONVERT(nvarchar(max), d.[Flag]), CONVERT(nvarchar(max), i.[Flag])),
            (N'isDeleted', CONVERT(nvarchar(max), d.[isDeleted]), CONVERT(nvarchar(max), i.[isDeleted])),
            (N'CreatedBy', CONVERT(nvarchar(max), d.[CreatedBy]), CONVERT(nvarchar(max), i.[CreatedBy])),
            (N'CreatedOn', CONVERT(nvarchar(max), d.[CreatedOn], 126), CONVERT(nvarchar(max), i.[CreatedOn], 126)),
            (N'DeletedBy', CONVERT(nvarchar(max), d.[DeletedBy]), CONVERT(nvarchar(max), i.[DeletedBy])),
            (N'DeletedOn', CONVERT(nvarchar(max), d.[DeletedOn], 126), CONVERT(nvarchar(max), i.[DeletedOn], 126)),
            (N'FamilyNotes', CONVERT(nvarchar(max), d.[FamilyNotes]), CONVERT(nvarchar(max), i.[FamilyNotes])),
            (N'SpecialInstructions', CONVERT(nvarchar(max), d.[SpecialInstructions]), CONVERT(nvarchar(max), i.[SpecialInstructions])),
            (N'LastUpdatedBy', CONVERT(nvarchar(max), d.[LastUpdatedBy]), CONVERT(nvarchar(max), i.[LastUpdatedBy])),
            (N'LastUpdatedat', CONVERT(nvarchar(max), d.[LastUpdatedat]), CONVERT(nvarchar(max), i.[LastUpdatedat])),
            (N'FatherNameUrdu', CONVERT(nvarchar(max), d.[FatherNameUrdu]), CONVERT(nvarchar(max), i.[FatherNameUrdu]))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_Staff ON dbo.[Staff]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Staff', i.[TeachersID], 'I', NULL, NULL,
           (SELECT i.[TeachersID] AS [TeachersID],
                    i.[FirstName] AS [FirstName],
                    i.[FatherName] AS [FatherName],
                    i.[Designation] AS [Designation],
                    i.[Address] AS [Address],
                    i.[HomePhone] AS [HomePhone],
                    i.[Birthdate] AS [Birthdate],
                    i.[DateHired] AS [DateHired],
                    i.[Salary] AS [Salary],
                    i.[Education] AS [Education],
                    i.[PrevExperience] AS [PrevExperience],
                    i.[Subjects] AS [Subjects],
                    i.[MaritalStatus] AS [MaritalStatus],
                    CASE WHEN i.[Password] IS NULL THEN NULL ELSE N'***' END AS [Password],
                    i.[Type] AS [Type],
                    i.[SLDate] AS [SLDate],
                    i.[Reason] AS [Reason],
                    i.[Flag] AS [Flag],
                    i.[TotalTestMarks] AS [TotalTestMarks],
                    i.[ObtainedTestMarks] AS [ObtainedTestMarks],
                    i.[RemarksofInterview] AS [RemarksofInterview],
                    i.[Reference] AS [Reference],
                    i.[CellNo] AS [CellNo],
                    i.[CurrentSalary] AS [CurrentSalary],
                    i.[HireSalary] AS [HireSalary],
                    i.[LastIncrement] AS [LastIncrement],
                    i.[SecDeduction] AS [SecDeduction],
                    i.[SCF] AS [SCF],
                    i.[Salute] AS [Salute],
                    i.[isDeleted] AS [isDeleted],
                    i.[CreatedBy] AS [CreatedBy],
                    i.[CreatedOn] AS [CreatedOn],
                    i.[DeletedBy] AS [DeletedBy],
                    i.[DeletedOn] AS [DeletedOn],
                    i.[StaffNameUrdu] AS [StaffNameUrdu]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[TeachersID] = i.[TeachersID]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Staff', d.[TeachersID], 'D', NULL,
           (SELECT d.[TeachersID] AS [TeachersID],
                    d.[FirstName] AS [FirstName],
                    d.[FatherName] AS [FatherName],
                    d.[Designation] AS [Designation],
                    d.[Address] AS [Address],
                    d.[HomePhone] AS [HomePhone],
                    d.[Birthdate] AS [Birthdate],
                    d.[DateHired] AS [DateHired],
                    d.[Salary] AS [Salary],
                    d.[Education] AS [Education],
                    d.[PrevExperience] AS [PrevExperience],
                    d.[Subjects] AS [Subjects],
                    d.[MaritalStatus] AS [MaritalStatus],
                    CASE WHEN d.[Password] IS NULL THEN NULL ELSE N'***' END AS [Password],
                    d.[Type] AS [Type],
                    d.[SLDate] AS [SLDate],
                    d.[Reason] AS [Reason],
                    d.[Flag] AS [Flag],
                    d.[TotalTestMarks] AS [TotalTestMarks],
                    d.[ObtainedTestMarks] AS [ObtainedTestMarks],
                    d.[RemarksofInterview] AS [RemarksofInterview],
                    d.[Reference] AS [Reference],
                    d.[CellNo] AS [CellNo],
                    d.[CurrentSalary] AS [CurrentSalary],
                    d.[HireSalary] AS [HireSalary],
                    d.[LastIncrement] AS [LastIncrement],
                    d.[SecDeduction] AS [SecDeduction],
                    d.[SCF] AS [SCF],
                    d.[Salute] AS [Salute],
                    d.[isDeleted] AS [isDeleted],
                    d.[CreatedBy] AS [CreatedBy],
                    d.[CreatedOn] AS [CreatedOn],
                    d.[DeletedBy] AS [DeletedBy],
                    d.[DeletedOn] AS [DeletedOn],
                    d.[StaffNameUrdu] AS [StaffNameUrdu]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[TeachersID] = d.[TeachersID]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Staff', i.[TeachersID], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[TeachersID] = i.[TeachersID]
    CROSS APPLY (VALUES
            (N'TeachersID', CONVERT(nvarchar(max), d.[TeachersID]), CONVERT(nvarchar(max), i.[TeachersID])),
            (N'FirstName', CONVERT(nvarchar(max), d.[FirstName]), CONVERT(nvarchar(max), i.[FirstName])),
            (N'FatherName', CONVERT(nvarchar(max), d.[FatherName]), CONVERT(nvarchar(max), i.[FatherName])),
            (N'Designation', CONVERT(nvarchar(max), d.[Designation]), CONVERT(nvarchar(max), i.[Designation])),
            (N'Address', CONVERT(nvarchar(max), d.[Address]), CONVERT(nvarchar(max), i.[Address])),
            (N'HomePhone', CONVERT(nvarchar(max), d.[HomePhone]), CONVERT(nvarchar(max), i.[HomePhone])),
            (N'Birthdate', CONVERT(nvarchar(max), d.[Birthdate], 126), CONVERT(nvarchar(max), i.[Birthdate], 126)),
            (N'DateHired', CONVERT(nvarchar(max), d.[DateHired], 126), CONVERT(nvarchar(max), i.[DateHired], 126)),
            (N'Salary', CONVERT(nvarchar(max), d.[Salary], 2), CONVERT(nvarchar(max), i.[Salary], 2)),
            (N'Education', CONVERT(nvarchar(max), d.[Education]), CONVERT(nvarchar(max), i.[Education])),
            (N'PrevExperience', CONVERT(nvarchar(max), d.[PrevExperience]), CONVERT(nvarchar(max), i.[PrevExperience])),
            (N'Subjects', CONVERT(nvarchar(max), d.[Subjects]), CONVERT(nvarchar(max), i.[Subjects])),
            (N'MaritalStatus', CONVERT(nvarchar(max), d.[MaritalStatus]), CONVERT(nvarchar(max), i.[MaritalStatus])),
            (N'Password', CASE WHEN d.[Password] IS NULL THEN NULL ELSE N'***' END, CASE WHEN i.[Password] IS NULL THEN NULL WHEN d.[Password] COLLATE Latin1_General_BIN2 = i.[Password] COLLATE Latin1_General_BIN2 THEN N'***' ELSE N'*** (changed)' END),
            (N'Type', CONVERT(nvarchar(max), d.[Type]), CONVERT(nvarchar(max), i.[Type])),
            (N'SLDate', CONVERT(nvarchar(max), d.[SLDate], 126), CONVERT(nvarchar(max), i.[SLDate], 126)),
            (N'Reason', CONVERT(nvarchar(max), d.[Reason]), CONVERT(nvarchar(max), i.[Reason])),
            (N'Flag', CONVERT(nvarchar(max), d.[Flag]), CONVERT(nvarchar(max), i.[Flag])),
            (N'TotalTestMarks', CONVERT(nvarchar(max), d.[TotalTestMarks]), CONVERT(nvarchar(max), i.[TotalTestMarks])),
            (N'ObtainedTestMarks', CONVERT(nvarchar(max), d.[ObtainedTestMarks]), CONVERT(nvarchar(max), i.[ObtainedTestMarks])),
            (N'RemarksofInterview', CONVERT(nvarchar(max), d.[RemarksofInterview]), CONVERT(nvarchar(max), i.[RemarksofInterview])),
            (N'Reference', CONVERT(nvarchar(max), d.[Reference]), CONVERT(nvarchar(max), i.[Reference])),
            (N'CellNo', CONVERT(nvarchar(max), d.[CellNo]), CONVERT(nvarchar(max), i.[CellNo])),
            (N'CurrentSalary', CONVERT(nvarchar(max), d.[CurrentSalary]), CONVERT(nvarchar(max), i.[CurrentSalary])),
            (N'HireSalary', CONVERT(nvarchar(max), d.[HireSalary], 2), CONVERT(nvarchar(max), i.[HireSalary], 2)),
            (N'LastIncrement', CONVERT(nvarchar(max), d.[LastIncrement], 2), CONVERT(nvarchar(max), i.[LastIncrement], 2)),
            (N'SecDeduction', CONVERT(nvarchar(max), d.[SecDeduction], 2), CONVERT(nvarchar(max), i.[SecDeduction], 2)),
            (N'SCF', CONVERT(nvarchar(max), d.[SCF], 2), CONVERT(nvarchar(max), i.[SCF], 2)),
            (N'Salute', CONVERT(nvarchar(max), d.[Salute]), CONVERT(nvarchar(max), i.[Salute])),
            (N'isDeleted', CONVERT(nvarchar(max), d.[isDeleted]), CONVERT(nvarchar(max), i.[isDeleted])),
            (N'CreatedBy', CONVERT(nvarchar(max), d.[CreatedBy]), CONVERT(nvarchar(max), i.[CreatedBy])),
            (N'CreatedOn', CONVERT(nvarchar(max), d.[CreatedOn], 126), CONVERT(nvarchar(max), i.[CreatedOn], 126)),
            (N'DeletedBy', CONVERT(nvarchar(max), d.[DeletedBy]), CONVERT(nvarchar(max), i.[DeletedBy])),
            (N'DeletedOn', CONVERT(nvarchar(max), d.[DeletedOn], 126), CONVERT(nvarchar(max), i.[DeletedOn], 126)),
            (N'StaffNameUrdu', CONVERT(nvarchar(max), d.[StaffNameUrdu]), CONVERT(nvarchar(max), i.[StaffNameUrdu]))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_Users ON dbo.[Users]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Users', i.[ID], 'I', NULL, NULL,
           (SELECT i.[ID] AS [ID],
                    i.[Login] AS [Login],
                    i.[FullName] AS [FullName],
                    CASE WHEN i.[Password] IS NULL THEN NULL ELSE N'***' END AS [Password],
                    i.[Email] AS [Email],
                    i.[LastLoginDate] AS [LastLoginDate],
                    i.[LastLoginTime] AS [LastLoginTime],
                    i.[LoginAttempts] AS [LoginAttempts],
                    i.[userGroup] AS [userGroup],
                    i.[BackupPath] AS [BackupPath]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[ID] = i.[ID]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Users', d.[ID], 'D', NULL,
           (SELECT d.[ID] AS [ID],
                    d.[Login] AS [Login],
                    d.[FullName] AS [FullName],
                    CASE WHEN d.[Password] IS NULL THEN NULL ELSE N'***' END AS [Password],
                    d.[Email] AS [Email],
                    d.[LastLoginDate] AS [LastLoginDate],
                    d.[LastLoginTime] AS [LastLoginTime],
                    d.[LoginAttempts] AS [LoginAttempts],
                    d.[userGroup] AS [userGroup],
                    d.[BackupPath] AS [BackupPath]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[ID] = d.[ID]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'Users', i.[ID], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[ID] = i.[ID]
    CROSS APPLY (VALUES
            (N'ID', CONVERT(nvarchar(max), d.[ID]), CONVERT(nvarchar(max), i.[ID])),
            (N'Login', CONVERT(nvarchar(max), d.[Login]), CONVERT(nvarchar(max), i.[Login])),
            (N'FullName', CONVERT(nvarchar(max), d.[FullName]), CONVERT(nvarchar(max), i.[FullName])),
            (N'Password', CASE WHEN d.[Password] IS NULL THEN NULL ELSE N'***' END, CASE WHEN i.[Password] IS NULL THEN NULL WHEN d.[Password] COLLATE Latin1_General_BIN2 = i.[Password] COLLATE Latin1_General_BIN2 THEN N'***' ELSE N'*** (changed)' END),
            (N'Email', CONVERT(nvarchar(max), d.[Email]), CONVERT(nvarchar(max), i.[Email])),
            (N'LastLoginDate', CONVERT(nvarchar(max), d.[LastLoginDate], 126), CONVERT(nvarchar(max), i.[LastLoginDate], 126)),
            (N'LastLoginTime', CONVERT(nvarchar(max), d.[LastLoginTime], 126), CONVERT(nvarchar(max), i.[LastLoginTime], 126)),
            (N'LoginAttempts', CONVERT(nvarchar(max), d.[LoginAttempts]), CONVERT(nvarchar(max), i.[LoginAttempts])),
            (N'userGroup', CONVERT(nvarchar(max), d.[userGroup]), CONVERT(nvarchar(max), i.[userGroup])),
            (N'BackupPath', CONVERT(nvarchar(max), d.[BackupPath]), CONVERT(nvarchar(max), i.[BackupPath]))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_tblAttendanceRegister ON dbo.[tblAttendanceRegister]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'tblAttendanceRegister', i.[RegisterID], 'I', NULL, NULL,
           (SELECT i.[RegisterID] AS [RegisterID],
                    i.[AttDate] AS [AttDate],
                    i.[ClassID] AS [ClassID],
                    i.[SectionID] AS [SectionID],
                    i.[TakenBy] AS [TakenBy],
                    i.[TakenOn] AS [TakenOn],
                    i.[IsLocked] AS [IsLocked],
                    i.[Remarks] AS [Remarks]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[RegisterID] = i.[RegisterID]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'tblAttendanceRegister', d.[RegisterID], 'D', NULL,
           (SELECT d.[RegisterID] AS [RegisterID],
                    d.[AttDate] AS [AttDate],
                    d.[ClassID] AS [ClassID],
                    d.[SectionID] AS [SectionID],
                    d.[TakenBy] AS [TakenBy],
                    d.[TakenOn] AS [TakenOn],
                    d.[IsLocked] AS [IsLocked],
                    d.[Remarks] AS [Remarks]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[RegisterID] = d.[RegisterID]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'tblAttendanceRegister', i.[RegisterID], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[RegisterID] = i.[RegisterID]
    CROSS APPLY (VALUES
            (N'RegisterID', CONVERT(nvarchar(max), d.[RegisterID]), CONVERT(nvarchar(max), i.[RegisterID])),
            (N'AttDate', CONVERT(nvarchar(max), d.[AttDate], 126), CONVERT(nvarchar(max), i.[AttDate], 126)),
            (N'ClassID', CONVERT(nvarchar(max), d.[ClassID]), CONVERT(nvarchar(max), i.[ClassID])),
            (N'SectionID', CONVERT(nvarchar(max), d.[SectionID]), CONVERT(nvarchar(max), i.[SectionID])),
            (N'TakenBy', CONVERT(nvarchar(max), d.[TakenBy]), CONVERT(nvarchar(max), i.[TakenBy])),
            (N'TakenOn', CONVERT(nvarchar(max), d.[TakenOn], 126), CONVERT(nvarchar(max), i.[TakenOn], 126)),
            (N'IsLocked', CONVERT(nvarchar(max), d.[IsLocked]), CONVERT(nvarchar(max), i.[IsLocked])),
            (N'Remarks', CONVERT(nvarchar(max), d.[Remarks]), CONVERT(nvarchar(max), i.[Remarks]))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
CREATE OR ALTER TRIGGER dbo.trg_Audit_tblStudentAttendance ON dbo.[tblStudentAttendance]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;

    DECLARE @now  datetime2(3)  = SYSDATETIME(),
            @user nvarchar(128) = ORIGINAL_LOGIN(),
            @win  nvarchar(128) = COALESCE(CONVERT(nvarchar(128), SESSION_CONTEXT(N'WindowsUser')),
                                           CASE WHEN ORIGINAL_LOGIN() LIKE N'%\%' THEN ORIGINAL_LOGIN() END),
            @host nvarchar(128) = HOST_NAME(),
            @app  nvarchar(128) = APP_NAME();

    -- Inserts: whole new row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'tblStudentAttendance', i.[AttendanceID], 'I', NULL, NULL,
           (SELECT i.[AttendanceID] AS [AttendanceID],
                    i.[RegisterID] AS [RegisterID],
                    i.[CC] AS [CC],
                    i.[StatusCode] AS [StatusCode],
                    i.[Remarks] AS [Remarks]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[AttendanceID] = i.[AttendanceID]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'tblStudentAttendance', d.[AttendanceID], 'D', NULL,
           (SELECT d.[AttendanceID] AS [AttendanceID],
                    d.[RegisterID] AS [RegisterID],
                    d.[CC] AS [CC],
                    d.[StatusCode] AS [StatusCode],
                    d.[Remarks] AS [Remarks]
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[AttendanceID] = d.[AttendanceID]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'tblStudentAttendance', i.[AttendanceID], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[AttendanceID] = i.[AttendanceID]
    CROSS APPLY (VALUES
            (N'AttendanceID', CONVERT(nvarchar(max), d.[AttendanceID]), CONVERT(nvarchar(max), i.[AttendanceID])),
            (N'RegisterID', CONVERT(nvarchar(max), d.[RegisterID]), CONVERT(nvarchar(max), i.[RegisterID])),
            (N'CC', CONVERT(nvarchar(max), d.[CC]), CONVERT(nvarchar(max), i.[CC])),
            (N'StatusCode', CONVERT(nvarchar(max), d.[StatusCode]), CONVERT(nvarchar(max), i.[StatusCode])),
            (N'Remarks', CONVERT(nvarchar(max), d.[Remarks]), CONVERT(nvarchar(max), i.[Remarks]))
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO


GO


/* ==================== 5 of 12: campus_info_install.sql ==================== */
PRINT N'5 of 12: campus_info_install';
GO
/* School details for printouts: the logo and the letterhead header and footer pictures, kept in CampusInfo. Safe to re-run.
   Logo: printed on the drawn letterhead and on ID cards.
   LetterheadHeader / LetterheadFooter: PNG pictures stretched across the top and bottom of certificates.
   When the header is empty, certificates draw their own letterhead (logo, school name, address and phones).
   Set them in the app: Setup › School information (Admin only). */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.CampusInfo', 'Logo') IS NULL
    ALTER TABLE dbo.CampusInfo ADD Logo varbinary(max) NULL;
IF COL_LENGTH('dbo.CampusInfo', 'LetterheadHeader') IS NULL
    ALTER TABLE dbo.CampusInfo ADD LetterheadHeader varbinary(max) NULL;
IF COL_LENGTH('dbo.CampusInfo', 'LetterheadFooter') IS NULL
    ALTER TABLE dbo.CampusInfo ADD LetterheadFooter varbinary(max) NULL;
GO

GO


/* ==================== 6 of 12: student_documents_install.sql ==================== */
PRINT N'6 of 12: student_documents_install';
GO
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

GO


/* ==================== 7 of 12: fee_receipt_files_install.sql ==================== */
PRINT N'7 of 12: fee_receipt_files_install';
GO
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

GO


/* ==================== 8 of 12: staff_payroll_install.sql ==================== */
PRINT N'8 of 12: staff_payroll_install';
GO
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

GO


/* ==================== 9 of 12: job_offer_install.sql ==================== */
PRINT N'9 of 12: job_offer_install';
GO
/* Job offer letters. Safe to re-run.
   1. Staff gets the details the letter asks for that were not kept before.
   2. tblJobOfferTemplates holds the wording an Admin has changed: one row for a post, or the row with no post for the
      general letter. A post with no row uses the general letter, and with no general row the school's sample wording. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.Staff', 'Caste') IS NULL
    ALTER TABLE dbo.Staff ADD
        Caste         nvarchar(50) NULL,
        Religion      nvarchar(50) NULL,
        CNIC          nvarchar(20) NULL,
        EmergencyNo   nvarchar(30) NULL,
        StPerk        money        NULL,
        FuelAllowance money        NULL;
GO

IF OBJECT_ID('dbo.tblJobOfferTemplates') IS NULL
BEGIN
    CREATE TABLE dbo.tblJobOfferTemplates (
        TemplateID  int            IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblJobOfferTemplates PRIMARY KEY,
        StaffTypeID int            NULL CONSTRAINT FK_tblJobOfferTemplates_StaffTypes REFERENCES dbo.StaffTypes (ID),
        Heading     nvarchar(100)  NOT NULL,
        CampusLine  nvarchar(100)  NOT NULL,
        Quote       nvarchar(200)  NOT NULL,
        Title       nvarchar(100)  NOT NULL,
        Intro       nvarchar(500)  NOT NULL,
        Terms       nvarchar(max)  NOT NULL,
        Signatory   nvarchar(100)  NOT NULL,
        Acceptance  nvarchar(1000) NOT NULL,
        UpdatedBy   nvarchar(50)   NULL,
        UpdatedOn   datetime       NULL
    );
    -- One letter for each post, and one general letter (no post).
    CREATE UNIQUE INDEX UX_tblJobOfferTemplates_StaffType ON dbo.tblJobOfferTemplates (StaffTypeID);
END
GO

GO


/* ==================== 10 of 12: staff_documents_install.sql ==================== */
PRINT N'10 of 12: staff_documents_install';
GO
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

GO


/* ==================== 11 of 12: staff_schedule_install.sql ==================== */
PRINT N'11 of 12: staff_schedule_install';
GO
/* Staff schedule: the check-in and check-out times of the staff for a period, and the minutes allowed before someone counts
   as late or as leaving early. Periods must not overlap; the app checks that. Safe to re-run. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.tblStaffSchedule') IS NULL
    CREATE TABLE dbo.tblStaffSchedule (
        ScheduleID    int           IDENTITY(1,1) NOT NULL CONSTRAINT PK_tblStaffSchedule PRIMARY KEY,
        ScheduleName  nvarchar(50)  NOT NULL,
        CheckIn       time(0)       NOT NULL,
        CheckOut      time(0)       NOT NULL,
        EffectiveFrom date          NOT NULL,
        EffectiveTo   date          NOT NULL,
        LateMinutes   smallint      NOT NULL CONSTRAINT DF_tblStaffSchedule_Late DEFAULT (0),
        EarlyMinutes  smallint      NOT NULL CONSTRAINT DF_tblStaffSchedule_Early DEFAULT (0),
        UpdatedBy     nvarchar(128) NULL,
        UpdatedOn     datetime2(0)  NULL,
        CONSTRAINT CK_tblStaffSchedule_Period CHECK (EffectiveTo >= EffectiveFrom),
        CONSTRAINT CK_tblStaffSchedule_Times CHECK (CheckOut > CheckIn),
        CONSTRAINT CK_tblStaffSchedule_Minutes CHECK (LateMinutes >= 0 AND EarlyMinutes >= 0)
    );
GO

GO


/* ==================== 12 of 12: messages_install.sql ==================== */
PRINT N'12 of 12: messages_install';
GO
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

GO


/* ==================== Check: everything should say OK ==================== */
SELECT What, CASE WHEN Present = 1 THEN 'OK' ELSE 'MISSING' END AS Result
FROM (VALUES
    ('AppUsers', CASE WHEN OBJECT_ID('dbo.AppUsers') IS NULL THEN 0 ELSE 1 END),
    ('tblUserScope', CASE WHEN OBJECT_ID('dbo.tblUserScope') IS NULL THEN 0 ELSE 1 END),
    ('tblAttendanceStatus', CASE WHEN OBJECT_ID('dbo.tblAttendanceStatus') IS NULL THEN 0 ELSE 1 END),
    ('tblAttendanceRegister', CASE WHEN OBJECT_ID('dbo.tblAttendanceRegister') IS NULL THEN 0 ELSE 1 END),
    ('tblStudentAttendance', CASE WHEN OBJECT_ID('dbo.tblStudentAttendance') IS NULL THEN 0 ELSE 1 END),
    ('tblSchoolCalendar', CASE WHEN OBJECT_ID('dbo.tblSchoolCalendar') IS NULL THEN 0 ELSE 1 END),
    ('vwStudentAttendanceMonthly', CASE WHEN OBJECT_ID('dbo.vwStudentAttendanceMonthly') IS NULL THEN 0 ELSE 1 END),
    ('tblStaffAttendance', CASE WHEN OBJECT_ID('dbo.tblStaffAttendance') IS NULL THEN 0 ELSE 1 END),
    ('trg_Audit_tblAttendanceRegister', CASE WHEN OBJECT_ID('dbo.trg_Audit_tblAttendanceRegister') IS NULL THEN 0 ELSE 1 END),
    ('trg_Audit_tblStudentAttendance', CASE WHEN OBJECT_ID('dbo.trg_Audit_tblStudentAttendance') IS NULL THEN 0 ELSE 1 END),
    ('CampusInfo.Logo', CASE WHEN COL_LENGTH('dbo.CampusInfo', 'Logo') IS NULL THEN 0 ELSE 1 END),
    ('tblStudentDocuments', CASE WHEN OBJECT_ID('dbo.tblStudentDocuments') IS NULL THEN 0 ELSE 1 END),
    ('tblFeeReceiptFiles', CASE WHEN OBJECT_ID('dbo.tblFeeReceiptFiles') IS NULL THEN 0 ELSE 1 END),
    ('Staff.BranchID', CASE WHEN COL_LENGTH('dbo.Staff', 'BranchID') IS NULL THEN 0 ELSE 1 END),
    ('tblSalaries.fk_SalHeadID', CASE WHEN COL_LENGTH('dbo.tblSalaries', 'fk_SalHeadID') IS NULL THEN 0 ELSE 1 END),
    ('tblSalariesHead.IsFinal', CASE WHEN COL_LENGTH('dbo.tblSalariesHead', 'IsFinal') IS NULL THEN 0 ELSE 1 END),
    ('tblStaffAdvance', CASE WHEN OBJECT_ID('dbo.tblStaffAdvance') IS NULL THEN 0 ELSE 1 END),
    ('Staff.CNIC', CASE WHEN COL_LENGTH('dbo.Staff', 'CNIC') IS NULL THEN 0 ELSE 1 END),
    ('tblJobOfferTemplates', CASE WHEN OBJECT_ID('dbo.tblJobOfferTemplates') IS NULL THEN 0 ELSE 1 END),
    ('tblStaffDocuments', CASE WHEN OBJECT_ID('dbo.tblStaffDocuments') IS NULL THEN 0 ELSE 1 END),
    ('tblStaffSchedule', CASE WHEN OBJECT_ID('dbo.tblStaffSchedule') IS NULL THEN 0 ELSE 1 END),
    ('Staff.Email', CASE WHEN COL_LENGTH('dbo.Staff', 'Email') IS NULL THEN 0 ELSE 1 END),
    ('tblMessageTemplates', CASE WHEN OBJECT_ID('dbo.tblMessageTemplates') IS NULL THEN 0 ELSE 1 END),
    ('tblMessageLog', CASE WHEN OBJECT_ID('dbo.tblMessageLog') IS NULL THEN 0 ELSE 1 END)
) AS c (What, Present);
GO
