const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/api/dailyAttendanceApi.ts";
let content = fs.readFileSync(path, "utf8");

content = content.replace("saveDeductionRequest: async", `approveAdjustment: async (personId: string, year: number, month: number, pinCode: number) =>
    (await api.post<{ message: string }>("/api/attendance/deductions/adjustment/approve", { personId, year, month, pinCode })).data,
  saveDeductionRequest: async`);

fs.writeFileSync(path, content, "utf8");
console.log("Done");

