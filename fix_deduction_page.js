const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    `import { FEATURE } from "../../lib/featureKeys";`,
    `import { FEATURE } from "../../lib/featureKeys";\nimport { staffApi } from "../../api/staffApi";`
);

content = content.replace(
    `  const openDeductionForm = () => {`,
    `  const { userName, userEmail } = useAuthStore();\n  const openDeductionForm = async () => {`
);

content = content.replace(
    `  const openDeductionForm = async () => {
    const selected = (instance()?.getSelectedRowsData?.() ?? []) as DeductionGridRow[];
    const row = selected[0];
    const period = parseMonth(monthValue);
    setError("");
    setForm(current => ({
      ...current,
      regNo: row?.staffNumber ?? current.regNo ?? "",
      name: row?.employeeName ?? current.name ?? "",
      userId: row?.staffNumber ?? current.userId ?? "",
      department: row?.department ?? current.department ?? "",
      designation: row?.jobTitle ?? current.designation ?? "",
      deductionMonth: period.month,
      deductionYear: period.year,
    }));
    setShowForm(true);
  };`,
    `  const openDeductionForm = async () => {
    const selected = (instance()?.getSelectedRowsData?.() ?? []) as DeductionGridRow[];
    const row = selected[0];
    const period = parseMonth(monthValue);
    setError("");
    
    // Fetch current user details
    let currentStaff = null;
    if (userEmail) {
      try {
        currentStaff = await staffApi.getByLogin(userEmail);
      } catch (e) {}
    }

    setForm(current => ({
      ...current,
      regNo: currentStaff?.staffNumber ?? row?.staffNumber ?? current.regNo ?? "",
      name: currentStaff?.fullName ?? row?.employeeName ?? userName ?? current.name ?? "",
      userId: currentStaff?.staffNumber ?? row?.staffNumber ?? current.userId ?? "",
      department: currentStaff?.departmentName ?? row?.department ?? current.department ?? "",
      designation: currentStaff?.jobTitle ?? row?.jobTitle ?? current.designation ?? "",
      office: currentStaff?.branchName ?? current.office ?? "",
      phone: currentStaff?.phone ?? current.phone ?? "",
      email: userEmail ?? current.email ?? "",
      deductionMonth: period.month,
      deductionYear: period.year,
    }));
    setShowForm(true);
  };`
);

fs.writeFileSync(path, content);
console.log("Fixed openDeductionForm");

