/* Refreshes the data in ArqamBase_dev from ArqamBase_be (production), table by table. Production is only read.
   Every dev table (schemas dbo and audit) that production also has is emptied and filled with production's rows;
   tables only dev has (the acc_ accounting tables) are left alone. Triggers and constraints are off during the copy,
   then back on. All in one transaction: if anything fails, dev stays as it was.
   Run in ArqamBase_dev by a login that is db_owner there and can read ArqamBase_be. */
SET NOCOUNT ON;
SET XACT_ABORT OFF; -- errors are caught below; the whole copy is rolled back on any failure
IF DB_NAME() <> N'ArqamBase_dev'
BEGIN
    RAISERROR('Run this in ArqamBase_dev only.', 16, 1);
    RETURN;
END

DECLARE @tables TABLE (id int IDENTITY, sch sysname, name sysname, fullName nvarchar(300), hasIdentity bit, cols nvarchar(max));
INSERT @tables (sch, name, fullName, hasIdentity, cols)
SELECT s.name, t.name, QUOTENAME(s.name) + N'.' + QUOTENAME(t.name),
       CASE WHEN EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.object_id AND c.is_identity = 1) THEN 1 ELSE 0 END,
       STUFF((SELECT N', ' + QUOTENAME(c.name)
              FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id
              WHERE c.object_id = t.object_id AND c.is_computed = 0 AND ty.name NOT IN (N'timestamp', N'rowversion')
              ORDER BY c.column_id FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'')
FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name IN (N'dbo', N'audit') AND t.name NOT LIKE N'acc[_]%'
  AND EXISTS (SELECT 1 FROM ArqamBase_be.sys.tables p JOIN ArqamBase_be.sys.schemas ps ON ps.schema_id = p.schema_id
              WHERE p.name = t.name AND ps.name = s.name);

DECLARE @i int = 1, @n int = (SELECT COUNT(*) FROM @tables), @full nvarchar(300), @ident bit, @cols nvarchar(max), @sql nvarchar(max);

BEGIN TRY
BEGIN TRANSACTION;

-- Triggers (audit) and constraints off on every table being copied.
WHILE @i <= @n
BEGIN
    SELECT @full = fullName FROM @tables WHERE id = @i;
    SET @sql = N'DISABLE TRIGGER ALL ON ' + @full + N'; ALTER TABLE ' + @full + N' NOCHECK CONSTRAINT ALL;';
    EXEC sp_executesql @sql;
    SET @i += 1;
END

-- Empty and fill each table.
SET @i = 1;
WHILE @i <= @n
BEGIN
    SELECT @full = fullName, @ident = hasIdentity, @cols = cols FROM @tables WHERE id = @i;
    SET @sql = N'DELETE ' + @full + N';'
        + CASE WHEN @ident = 1 THEN N' SET IDENTITY_INSERT ' + @full + N' ON;' ELSE N'' END
        + N' INSERT ' + @full + N' (' + @cols + N') SELECT ' + @cols + N' FROM ArqamBase_be.' + @full + N';'
        + CASE WHEN @ident = 1 THEN N' SET IDENTITY_INSERT ' + @full + N' OFF;' ELSE N'' END;
    EXEC sp_executesql @sql;
    IF @ident = 1
    BEGIN
        SET @sql = N'DBCC CHECKIDENT (''' + REPLACE(@full, '''', '''''') + N''', RESEED) WITH NO_INFOMSGS;';
        EXEC sp_executesql @sql;
    END
    SET @i += 1;
END

-- Constraints and triggers back on. Constraints are re-checked where production's data allows it.
SET @i = 1;
WHILE @i <= @n
BEGIN
    SELECT @full = fullName FROM @tables WHERE id = @i;
    BEGIN TRY
        SET @sql = N'ALTER TABLE ' + @full + N' WITH CHECK CHECK CONSTRAINT ALL;';
        EXEC sp_executesql @sql;
    END TRY
    BEGIN CATCH
        SET @sql = N'ALTER TABLE ' + @full + N' CHECK CONSTRAINT ALL;';
        EXEC sp_executesql @sql;
        PRINT N'Constraints of ' + @full + N' are on but not re-checked: ' + ERROR_MESSAGE();
    END CATCH
    SET @sql = N'ENABLE TRIGGER ALL ON ' + @full + N';';
    EXEC sp_executesql @sql;
    SET @i += 1;
END

COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH

-- Row counts: dev against production.
SELECT t.sch + N'.' + t.name AS [table], d.rows AS dev, p.rows AS production, CASE WHEN d.rows = p.rows THEN '' ELSE 'DIFFERENT' END AS [check]
FROM @tables t
CROSS APPLY (SELECT SUM(x.rows) AS rows FROM sys.partitions x WHERE x.object_id = OBJECT_ID(t.fullName) AND x.index_id IN (0, 1)) d
CROSS APPLY (SELECT SUM(x.rows) AS rows FROM ArqamBase_be.sys.partitions x JOIN ArqamBase_be.sys.tables y ON y.object_id = x.object_id
             JOIN ArqamBase_be.sys.schemas ys ON ys.schema_id = y.schema_id
             WHERE y.name = t.name AND ys.name = t.sch AND x.index_id IN (0, 1)) p
ORDER BY t.sch, t.name;
