const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

const approveFunction = `
  const handleApproveSubmit = async () => {
    if (!approvalRow || !pinCode) return;
    try {
      const period = parseMonth(monthValue);
      await dailyAttendanceApi.approveAdjustment(approvalRow.personId, period.year, period.month, parseInt(pinCode));
      window.alert("Adjustment approved successfully.");
      setShowApprovalModal(false);
      setPinCode("");
      setApprovalRow(null);
      await loadRows(period.year, period.month);
    } catch (e: any) {
      window.alert(e?.response?.data?.message || "Failed to approve adjustment.");
    }
  };
`;
content = content.replace("const saveAdjustment = async", approveFunction + "\n  const saveAdjustment = async");
fs.writeFileSync(path, content, "utf8");
console.log("Done");

