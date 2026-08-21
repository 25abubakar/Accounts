const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/api/dailyAttendanceApi.ts";
let content = fs.readFileSync(path, "utf8");

const oldStr = "saveAdjustment: async (personId: string, year: number, month: number, adjustmentAmount: number | null, remarks: string | null) =>\\n    (await api.post<{ message: string }>(\\"/api/attendance/deductions/adjustment\\", { personId, year, month, adjustmentAmount, remarks })).data,";
const newStr = `saveAdjustment: async (personId: string, year: number, month: number, adjustmentAmount: number | null, remarks: string | null) =>
    (await api.post<{ message: string }>("/api/attendance/deductions/adjustment", { personId, year, month, adjustmentAmount, remarks })).data,
  approveAdjustment: async (personId: string, year: number, month: number, pinCode: number) =>
    (await api.post<{ message: string }>("/api/attendance/deductions/adjustment/approve", { personId, year, month, pinCode })).data,`;

content = content.replace(oldStr, newStr);
fs.writeFileSync(path, content, "utf8");
console.log("Done");

