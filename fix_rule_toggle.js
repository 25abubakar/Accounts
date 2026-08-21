const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/RulesPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    `<ToggleField label="Is Active" description="Available for attendance calculation" checked={form.isActive} onChange={value => set("isActive", value)}/>`,
    `<ToggleField label="Is Active" description="Available for attendance calculation" checked={form.isActive} onChange={value => set("isActive", value)}/>
          <ToggleField label="Overtime Bonus" description="Pay bonus for overtime hours" checked={form.isOvertimeBonusActive} onChange={value => set("isOvertimeBonusActive", value)}/>`
);

fs.writeFileSync(path, content);
console.log("Success");

