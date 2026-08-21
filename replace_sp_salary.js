const fs = require("fs");
const path = "Accounts/Data/AttendanceRecordSchema.cs";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    "CASE WHEN s.IsOvertimeApproved = 1 THEN (c.NetOvertimeMinutes / 60.0) * c.PerHour ELSE 0 END",
    "CASE WHEN s.IsOvertimeApproved = 1 AND c.IsOvertimeBonusActive = 1 THEN (c.NetOvertimeMinutes / 60.0) * c.PerHour ELSE 0 END"
);

fs.writeFileSync(path, content);
console.log("Success");

