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
