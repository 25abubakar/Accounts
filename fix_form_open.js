const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    `const openDeductionForm = async () => {
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
  };`,
    `const openDeductionForm = async () => {
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
console.log("Fixed setShowForm(true)");

