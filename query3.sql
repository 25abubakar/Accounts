DECLARE @VisiblePersonIds nvarchar(max) = '["3BFCAABC-406C-4AF8-B0D9-2F075DC6432E", "416EF9C3-24C5-4F8F-8E1A-8E1F7086A7CF"]';
DECLARE @TenantId int = 2007;

WITH VisiblePeople AS (
    SELECT TRY_CONVERT(uniqueidentifier,[value]) PersonId FROM OPENJSON(@VisiblePersonIds)
    WHERE TRY_CONVERT(uniqueidentifier,[value]) IS NOT NULL
)
SELECT person.FullName, staff.StaffId
FROM VisiblePeople visible
JOIN dbo.Persons person ON person.PersonId = visible.PersonId AND person.TenantId = @TenantId AND person.IsActive = 1
JOIN dbo.StaffVacancy staff ON staff.PersonId = person.PersonId AND staff.TenantId = @TenantId
