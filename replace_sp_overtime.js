const fs = require("fs");
const path = "Accounts/Data/AttendanceRecordSchema.cs";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    "ruleSetting.WorkingMinutes AS RuleWorkingMinutes", 
    "ruleSetting.WorkingMinutes AS RuleWorkingMinutes,\n                            COALESCE(ruleSetting.IsOvertimeBonusActive, 0) AS IsOvertimeBonusActive"
);

content = content.replace(
    "staff.CurrentPay,",
    "staff.CurrentPay,\n                            staff.IsOvertimeBonusActive,"
);

content = content.replace(
    "MAX(CurrentPay) AS CurrentPay,",
    "MAX(CurrentPay) AS CurrentPay,\n            CAST(MAX(CAST(IsOvertimeBonusActive AS int)) AS bit) AS IsOvertimeBonusActive,"
);

content = content.replace(
    "CAST((c.NetOvertimeMinutes / 60.0) * c.PerHour AS decimal(18,2)) AS OvertimeBonusAmount,",
    "CAST(CASE WHEN c.IsOvertimeBonusActive = 1 THEN (c.NetOvertimeMinutes / 60.0) * c.PerHour ELSE 0 END AS decimal(18,2)) AS OvertimeBonusAmount,"
);

fs.writeFileSync(path, content);
console.log("Success");

