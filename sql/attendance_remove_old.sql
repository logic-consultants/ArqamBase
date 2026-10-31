/* Drops the old Access attendance tables.
   Run ONLY after the new app's attendance screen is in use and the Access
   attendance forms are retired. Data was copied by attendance_install.sql. */
DROP TABLE IF EXISTS dbo.tblAttendanceChild;
DROP TABLE IF EXISTS dbo.tblAttendanceMaster;
GO
