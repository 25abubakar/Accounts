const fs = require("fs");
let content = fs.readFileSync("Accounts/Migrations/20260724165000_AddAttendanceDeductionReportProcedure.cs", "utf8");

// Extract the SQL string from the migration
let sqlStart = content.indexOf("CREATE OR ALTER PROCEDURE");
if (sqlStart === -1) sqlStart = content.indexOf("CREATE PROCEDURE");

let sqlEnd = content.indexOf("\"\"\");", sqlStart);
let sql = content.substring(sqlStart, sqlEnd);

// Save it
fs.writeFileSync("original_sp.sql", sql, "utf8");
console.log("Written original_sp.sql");

