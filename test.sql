DECLARE @Json nvarchar(max) = '["3BFCAABC-406C-4AF8-B0D9-2F075DC6432E"]'; SELECT TRY_CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@Json);
