/* App users for the new desktop app. Safe to re-run.
   Users sign in to the app with a user name and password (stored as a salted PBKDF2 hash, never the password itself),
   then connect to SQL Server with their own Windows account on DAS-SVR.
   Roles: Admin, FeeClerk, Admissions, ReadOnly. When the table is empty, the first person to sign in creates the Admin account. */

-- Replace the first version of the table (keyed on Windows login), which only ever existed on ArqamBase_dev.
IF COL_LENGTH('dbo.AppUsers', 'WindowsLogin') IS NOT NULL
    DROP TABLE dbo.AppUsers;
GO

IF OBJECT_ID('dbo.AppUsers') IS NULL
    CREATE TABLE dbo.AppUsers (
        UserName           nvarchar(50)   NOT NULL CONSTRAINT PK_AppUsers PRIMARY KEY,
        FullName           nvarchar(100)  NULL,
        RoleName           varchar(20)    NOT NULL CONSTRAINT CK_AppUsers_Role CHECK (RoleName IN ('Admin','FeeClerk','Admissions','ReadOnly')),
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
