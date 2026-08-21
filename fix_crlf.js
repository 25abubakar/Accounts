const fs = require("fs");
const path = "Accounts/Data/AttendanceRecordSchema.cs";
let content = fs.readFileSync(path, "utf8");
content = content.replace(/\r\n/g, "\n").replace(/\n/g, "\r\n");
fs.writeFileSync(path, content);

