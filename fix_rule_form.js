const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/RulesPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    "isActive: boolean;\n  remarks: string;",
    "isActive: boolean;\n  isOvertimeBonusActive: boolean;\n  remarks: string;"
);

content = content.replace(
    "isActive: initialRule?.isActive ?? true,",
    "isActive: initialRule?.isActive ?? true,\n      isOvertimeBonusActive: initialRule?.isOvertimeBonusActive ?? false,"
);

content = content.replace(
    "isActive: form.isActive,\n      remarks: form.remarks.trim() || null,",
    "isActive: form.isActive,\n      isOvertimeBonusActive: form.isOvertimeBonusActive,\n      remarks: form.remarks.trim() || null,"
);

content = content.replace(
    "<label className=\"flex h-10 cursor-pointer items-center gap-2 rounded-lg border border-slate-300 bg-white px-3 text-xs font-extrabold text-slate-700 transition hover:border-sky-400\">\n            <input type=\"checkbox\" checked={form.isActive} onChange={event => set(\"isActive\", event.target.checked)} className=\"h-4 w-4 rounded border-slate-300 accent-sky-500\"/>\n            Is Active\n          </label>",
    `<label className="flex h-10 cursor-pointer items-center gap-2 rounded-lg border border-slate-300 bg-white px-3 text-xs font-extrabold text-slate-700 transition hover:border-sky-400">
            <input type="checkbox" checked={form.isActive} onChange={event => set("isActive", event.target.checked)} className="h-4 w-4 rounded border-slate-300 accent-sky-500"/>
            Is Active
          </label>
          <label className="flex h-10 cursor-pointer items-center gap-2 rounded-lg border border-slate-300 bg-white px-3 text-xs font-extrabold text-slate-700 transition hover:border-sky-400">
            <input type="checkbox" checked={form.isOvertimeBonusActive} onChange={event => set("isOvertimeBonusActive", event.target.checked)} className="h-4 w-4 rounded border-slate-300 accent-sky-500"/>
            Overtime Bonus
          </label>`
);

fs.writeFileSync(path, content);
console.log("Success");

