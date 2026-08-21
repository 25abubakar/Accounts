const fs = require("fs");
let cs = fs.readFileSync("Accounts/Migrations/20260821063512_AddProcessApprovalCode.cs", "utf8");
let sql = fs.readFileSync("update_sp_final9.sql", "utf8");
let startIndex = cs.indexOf("migrationBuilder.Sql(@\"");
if (startIndex !== -1) {
    let endIndex = cs.indexOf("\");", startIndex + 25);
    cs = cs.substring(0, startIndex) + "migrationBuilder.Sql(@\"" + sql.replace(/"/g, "\"\"") + "\");" + cs.substring(endIndex + 3);
}
fs.writeFileSync("Accounts/Migrations/20260821063512_AddProcessApprovalCode.cs", cs, "utf8");

