const fs = require("fs");
const path = "Accounts/Data/AttendanceRecordSchema.cs";
let content = fs.readFileSync(path, "utf8");

content = content.replace("                            staff.IsOvertimeBonusActive,", "                              staff.IsOvertimeBonusActive,");
content = content.replace("            CAST(MAX(CAST(IsOvertimeBonusActive AS int)) AS bit) AS IsOvertimeBonusActive,", "                              CAST(MAX(CAST(IsOvertimeBonusActive AS int)) AS bit) AS IsOvertimeBonusActive,");
fs.writeFileSync(path, content);

