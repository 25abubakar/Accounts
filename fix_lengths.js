const fs = require("fs");
let sql = fs.readFileSync("update_sp_final6.sql", "utf8");

sql = sql.replace("CAST(N'' AS nvarchar(max)) AS StaffNumber,", "CAST(N'' AS nvarchar(50)) AS StaffNumber,");
sql = sql.replace("CAST(N'' AS nvarchar(max)) AS EmployeeName,", "CAST(N'' AS nvarchar(200)) AS EmployeeName,");
sql = sql.replace("CAST(N'' AS nvarchar(max)) AS JobTitle,", "CAST(N'' AS nvarchar(150)) AS JobTitle,");
sql = sql.replace("CAST(N'' AS nvarchar(max)) AS Department,", "CAST(N'' AS nvarchar(200)) AS Department,");

fs.writeFileSync("update_sp_final7.sql", sql, "utf8");
console.log("Written update_sp_final7.sql");

