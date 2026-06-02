/*
  Run this in SSMS against your SQL Server DB after applying migration:
    20260602120317_RefactorStaffVacancyAndPersons

  Purpose:
  - Find views/procs/functions/triggers that still reference:
      - Staff (old table name)
      - Persons.LoginId / Persons.BranchId (dropped columns)
  - Help you update them to:
      - StaffVacancy
      - StaffVacancy.LoginId
      - Vacancy.OrganizationId (instead of Persons.BranchId) where applicable
*/

SET NOCOUNT ON;

DECLARE @Needle TABLE (Pattern nvarchar(200));
INSERT INTO @Needle (Pattern) VALUES
 (N'[dbo].[Staff]'),
 (N' Staff '),
 (N'FROM Staff'),
 (N'JOIN Staff'),
 (N'[Staff]'),
 (N'Persons.LoginId'),
 (N'[Persons].[LoginId]'),
 (N'Persons.BranchId'),
 (N'[Persons].[BranchId]'),
 (N'BranchId'),
 (N'LoginId');

PRINT '--- 1) Search SQL modules for old references ---';
SELECT
    o.[type_desc],
    QUOTENAME(OBJECT_SCHEMA_NAME(o.object_id)) + N'.' + QUOTENAME(o.name) AS [object_name],
    n.Pattern
FROM sys.objects o
JOIN sys.sql_modules m ON m.object_id = o.object_id
CROSS JOIN @Needle n
WHERE o.is_ms_shipped = 0
  AND m.[definition] LIKE N'%' + n.Pattern + N'%'
ORDER BY o.[type_desc], [object_name], n.Pattern;

PRINT '--- 2) Expression dependencies (if present) ---';
SELECT
    referencing_schema_name = OBJECT_SCHEMA_NAME(d.referencing_id),
    referencing_entity_name = OBJECT_NAME(d.referencing_id),
    referenced_schema_name  = d.referenced_schema_name,
    referenced_entity_name  = d.referenced_entity_name,
    referenced_minor_name   = d.referenced_minor_name
FROM sys.sql_expression_dependencies d
WHERE (d.referenced_entity_name IN (N'Staff', N'Persons')
       OR d.referenced_minor_name IN (N'LoginId', N'BranchId'))
ORDER BY referencing_schema_name, referencing_entity_name;

PRINT '--- 3) Refresh views (optional) ---';
-- This will error for views with broken definitions; that’s useful for detection.
DECLARE @sql nvarchar(max) = N'';
SELECT @sql = @sql + N'EXEC sp_refreshview ''' +
              QUOTENAME(OBJECT_SCHEMA_NAME(object_id)) + N'.' + QUOTENAME(name) + N''';' + CHAR(10)
FROM sys.views
WHERE is_ms_shipped = 0;
-- PRINT @sql;
EXEC sp_executesql @sql;

