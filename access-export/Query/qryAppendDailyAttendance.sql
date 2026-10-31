INSERT INTO tblAttendanceChild ( FK_cc, Name, FatherName, fk_ClassID, fk_SectionID, Attendance, fk_Attd_Master_ID )
SELECT qryfetchStudentsattendance.CC, qryfetchStudentsattendance.Name, qryfetchStudentsattendance.FatherName, qryfetchStudentsattendance.CurrentClass, qryfetchStudentsattendance.CurrentSection, "P" AS AttendStatus, [Forms]![tblAttendanceMaster]![txtAttd_ID] AS Attd_Master_ID
FROM qryfetchStudentsattendance;

