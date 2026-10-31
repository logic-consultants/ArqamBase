# Generates sql/audit_install.sql and sql/audit_remove.sql from the live table definitions.
# Usage: .\gen-audit.ps1 -Database ArqamBase_dev   (scripts have no USE: run them on the DB selected in SSMS)
param([string]$OutDir = (Split-Path $PSScriptRoot), [string]$Database = 'ArqamBase_dev')
$env:SQLCMDPASSWORD = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','User')

$tables  = 'Feerecipt','FeeDefinitation','Students','Family','Staff','Users','tblAttendanceRegister','tblStudentAttendance'
$skip    = 'TS'                      # rowversion / Access timestamp columns
$masked  = 'Password','Picture'      # log that it changed, never the value

$q = "SET NOCOUNT ON; SELECT t.name, c.name, ty.name, c.is_identity FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE t.name IN ('" + ($tables -join "','") + "') ORDER BY t.name, c.column_id"
$rows = sqlcmd -S DAS-SVR -d $Database -U claude_readonly -l 15 -h -1 -W -s "|" -Q $q |
  Where-Object { $_ -match '\|' } |
  ForEach-Object { $p = $_.Split('|'); [pscustomobject]@{ T=$p[0]; C=$p[1]; Type=$p[2]; Ident=$p[3] -eq '1' } }

function Conv($alias, $c) {
  $x = "$alias.[$($c.C)]"
  switch -Regex ($c.Type) {
    '^(date|datetime|datetime2|smalldatetime)$' { "CONVERT(nvarchar(max), $x, 126)" }
    '^float$'                                  { "CONVERT(nvarchar(max), $x, 3)" }
    '^(money|smallmoney)$'                     { "CONVERT(nvarchar(max), $x, 2)" }
    default                                    { "CONVERT(nvarchar(max), $x)" }
  }
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine(@"
/* Audit trail for ArqamBase_be
   Logs every insert, update and delete on: $($tables -join ', ')
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
"@)

foreach ($t in $tables) {
  $cols = $rows | Where-Object { $_.T -eq $t }
  $key  = ($cols | Where-Object Ident | Select-Object -First 1).C
  $aud  = $cols | Where-Object { $_.C -notin $skip }

  $json = ($aud | ForEach-Object {
    if ($_.C -in $masked) { "CASE WHEN {0}.[$($_.C)] IS NULL THEN NULL ELSE N'***' END AS [$($_.C)]" }
    else { "{0}.[$($_.C)] AS [$($_.C)]" } }) -join ",`n                    "

  $vals = ($aud | ForEach-Object {
    if ($_.C -in $masked) {
      "(N'$($_.C)', CASE WHEN d.[$($_.C)] IS NULL THEN NULL ELSE N'***' END, CASE WHEN i.[$($_.C)] IS NULL THEN NULL WHEN d.[$($_.C)] COLLATE Latin1_General_BIN2 = i.[$($_.C)] COLLATE Latin1_General_BIN2 THEN N'***' ELSE N'*** (changed)' END)"
    } else { "(N'$($_.C)', $(Conv 'd' $_), $(Conv 'i' $_))" } }) -join ",`n            "

  [void]$sb.AppendLine(@"
CREATE OR ALTER TRIGGER dbo.trg_Audit_$t ON dbo.[$t]
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
    SELECT @now, N'$t', i.[$key], 'I', NULL, NULL,
           (SELECT $($json -f 'i')
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           @user, @win, @host, @app
    FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[$key] = i.[$key]);

    -- Deletes: whole old row
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'$t', d.[$key], 'D', NULL,
           (SELECT $($json -f 'd')
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
           NULL, @user, @win, @host, @app
    FROM deleted d
    WHERE NOT EXISTS (SELECT 1 FROM inserted i WHERE i.[$key] = d.[$key]);

    -- Updates: one row per changed column (case-sensitive compare)
    INSERT audit.AuditLog (ChangedAt, TableName, KeyValue, Action, ColumnName, OldValue, NewValue, SqlLogin, WindowsUser, ComputerName, AppName)
    SELECT @now, N'$t', i.[$key], 'U', v.Col, v.OldV, v.NewV, @user, @win, @host, @app
    FROM inserted i
    JOIN deleted d ON d.[$key] = i.[$key]
    CROSS APPLY (VALUES
            $vals
        ) v (Col, OldV, NewV)
    WHERE EXISTS (SELECT v.OldV COLLATE Latin1_General_BIN2 EXCEPT SELECT v.NewV COLLATE Latin1_General_BIN2);
END
GO
"@)
}

$sb.ToString() | Set-Content -Encoding utf8 (Join-Path $OutDir 'audit_install.sql')

$rm = "/* Removes the audit triggers. The audit.AuditLog table and its history are kept. */`n" +
  (($tables | ForEach-Object { "DROP TRIGGER IF EXISTS dbo.trg_Audit_$_;" }) -join "`n") + "`nGO`n"
$rm | Set-Content -Encoding utf8 (Join-Path $OutDir 'audit_remove.sql')
