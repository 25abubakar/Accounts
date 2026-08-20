const fs = require("fs");
const path = "Accounts/Data/AttendanceRecordSchema.cs";
let content = fs.readFileSync(path, "utf8");

const start = content.indexOf(`CREATE OR ALTER PROCEDURE dbo.usp_Attendance_DeductionReport`);
const endStr = `OPTION (MAXRECURSION 31);\n                END`;
let end = content.indexOf(`OPTION (MAXRECURSION 31);\r\n                END`);
if (end === -1) end = content.indexOf(`OPTION (MAXRECURSION 31);\n                END`);

if (start !== -1 && end !== -1) {
    const sp = fs.readFileSync("Accounts/scratch_sp.sql", "utf8");
    const indentedSp = sp.trim().split("\n").map((l, i) => i === 0 ? l : "                " + l).join("\n");
    content = content.substring(0, start) + indentedSp + "\n" + content.substring(end + endStr.length);
    fs.writeFileSync(path, content);
    console.log("Replaced successfully!");
} else {
    console.log("Could not find start or end.", start, end);
}

