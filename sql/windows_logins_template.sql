/* SQL Server access for one staff member (workgroup, no domain). Safe to re-run.
   Before running: on DAS-SVR create a local Windows user for them (Computer Management > Local Users and Groups).
   It does not need to match their PC account: they type this user and password in step 2 of the ArqamBase sign-in.
   Then run this on DAS-SVR as sysadmin. */
USE [master];
GO
IF SUSER_ID(N'DAS-SVR\Abid') IS NULL
    CREATE LOGIN [DAS-SVR\Abid] FROM WINDOWS WITH DEFAULT_DATABASE = [ArqamBase_dev];
GO
USE [ArqamBase_dev];
GO
IF USER_ID(N'DAS-SVR\Abid') IS NULL
    CREATE USER [DAS-SVR\Abid] FOR LOGIN [DAS-SVR\Abid];
ALTER ROLE db_datareader ADD MEMBER [DAS-SVR\Abid];
ALTER ROLE db_datawriter ADD MEMBER [DAS-SVR\Abid];
GO
/* Then in the app: Setup lists > Users & roles, add their ArqamBase user name and role, and press Set password.
   For a Coordinator or Teacher in charge, save, then press Assign classes to tick their departments or class and section. */
