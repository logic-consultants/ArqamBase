/* Removes the audit triggers. The audit.AuditLog table and its history are kept. */
DROP TRIGGER IF EXISTS dbo.trg_Audit_Feerecipt;
DROP TRIGGER IF EXISTS dbo.trg_Audit_FeeDefinitation;
DROP TRIGGER IF EXISTS dbo.trg_Audit_Students;
DROP TRIGGER IF EXISTS dbo.trg_Audit_Family;
DROP TRIGGER IF EXISTS dbo.trg_Audit_Staff;
DROP TRIGGER IF EXISTS dbo.trg_Audit_Users;
DROP TRIGGER IF EXISTS dbo.trg_Audit_tblAttendanceRegister;
DROP TRIGGER IF EXISTS dbo.trg_Audit_tblStudentAttendance;
GO

