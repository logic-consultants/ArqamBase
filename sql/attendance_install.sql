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
