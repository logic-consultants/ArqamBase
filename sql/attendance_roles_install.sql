/* Attendance, part 2: Coordinator and Teacher in charge roles, what each user is assigned to, and staff attendance.
   Run as a database owner after app_users_install.sql and attendance_install.sql. Safe to re-run.
   Coordinators mark and edit attendance of their departments; a teacher in charge marks their own class and section.
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
