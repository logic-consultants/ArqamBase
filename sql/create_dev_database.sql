/* Creates ArqamBase_dev as a full copy of ArqamBase_be (schema, data, triggers, audit log).
   Run as sysadmin on DAS-SVR. Production is not changed: COPY_ONLY does not affect its backup chain. */
USE [master];
GO

BACKUP DATABASE [ArqamBase_be]
TO DISK = N'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ArqamBase_be_for_dev.bak'
WITH COPY_ONLY, INIT, COMPRESSION, CHECKSUM, STATS = 10;
GO

RESTORE DATABASE [ArqamBase_dev]
FROM DISK = N'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\Backup\ArqamBase_be_for_dev.bak'
WITH MOVE N'ArqamBase_BE'     TO N'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ArqamBase_dev.mdf',
     MOVE N'ArqamBase_BE_log' TO N'C:\Program Files\Microsoft SQL Server\MSSQL16.MSSQLSERVER\MSSQL\DATA\ArqamBase_dev_log.ldf',
     CHECKSUM, STATS = 10;
GO

-- Dev database: simple recovery and a small log
ALTER DATABASE [ArqamBase_dev] SET RECOVERY SIMPLE;
GO
USE [ArqamBase_dev];
GO
DBCC SHRINKFILE (N'ArqamBase_BE_log', 64);
GO

-- Claude's login: full rights on the dev database only (stays read-only on ArqamBase_be)
ALTER ROLE db_owner ADD MEMBER claude_readonly;
GO

-- Quick check: row counts should match production
SELECT 'ArqamBase_dev' AS db, COUNT(*) AS tables,
       (SELECT SUM(p.rows) FROM sys.partitions p JOIN sys.tables t ON t.object_id = p.object_id WHERE p.index_id IN (0,1)) AS total_rows
FROM sys.tables;
GO
