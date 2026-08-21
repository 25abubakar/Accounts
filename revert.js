const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    `<div className="mb-3 overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
            <div className="flex items-center justify-between bg-sky-500 px-4 py-3 text-white">
              <div>
                <h2 className="text-sm font-black tracking-wide">Deduction Registration</h2>
                <p className="text-xs font-semibold text-sky-50">Submit salary deduction process request</p>
              </div>
            </div>`,
    `{showForm && (
            <div className="mb-3 overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
              <div className="flex items-center justify-between bg-sky-500 px-4 py-3 text-white">
                <div>
                  <h2 className="text-sm font-black tracking-wide">Deduction Registration</h2>
                  <p className="text-xs font-semibold text-sky-50">Submit salary deduction process request</p>
                </div>
                <button type="button" onClick={() => setShowForm(false)} className="rounded-lg p-1.5 transition hover:bg-white/15" aria-label="Close deduction form">
                  <X size={18}/>
                </button>
              </div>`
);

content = content.replace(
    `            <div className="flex items-end justify-end">
                  <button type="button" disabled={saving} onClick={submitDeductionForm} className="flex h-10 items-center gap-2 rounded-lg bg-[#27333d] px-6 text-sm font-bold text-white shadow-sm transition hover:bg-slate-700 disabled:opacity-50">
                    <Save size={16}/> Submit
                  </button>
                </div>
              </div>
            </div>`,
    `            <div className="flex items-end justify-end">
                  <button type="button" disabled={saving} onClick={submitDeductionForm} className="flex h-10 items-center gap-2 rounded-lg bg-[#27333d] px-6 text-sm font-bold text-white shadow-sm transition hover:bg-slate-700 disabled:opacity-50">
                    <Save size={16}/> Submit
                  </button>
                </div>
              </div>
            </div>
          )}`
);

content = content.replace(/setShowForm\(false\);/g, "");
content = content.replace(/showSuccess\(result.message \|\| "Deduction request submitted successfully."\);/g, "setShowForm(false);\n        showSuccess(result.message || \"Deduction request submitted successfully.\");");

content = content.replace(
    `useEffect(() => {
    const initForm = async () => {
      let currentStaff = null;
      if (userEmail) {
        try {
          currentStaff = await staffApi.getByLogin(userEmail);
        } catch (e) {}
      }
      if (currentStaff) {
        setForm(current => ({
          ...current,
          regNo: currentStaff?.staffNumber ?? current.regNo ?? "",
          name: currentStaff?.fullName ?? userName ?? current.name ?? "",
          userId: currentStaff?.staffNumber ?? current.userId ?? "",
          department: currentStaff?.departmentName ?? current.department ?? "",
          designation: currentStaff?.jobTitle ?? current.designation ?? "",
          office: currentStaff?.branchName ?? current.office ?? "",
          phone: currentStaff?.phone ?? current.phone ?? "",
          email: userEmail ?? current.email ?? "",
        }));
      }
    };
    initForm();
  }, [userEmail, userName]);

  const openDeductionForm = async () => {
    const selected = (instance()?.getSelectedRowsData?.() ?? []) as DeductionGridRow[];`,
    `const openDeductionForm = async () => {
    const selected = (instance()?.getSelectedRowsData?.() ?? []) as DeductionGridRow[];`
);

content = content.replace(
    `const row = selected[0];
    const period = parseMonth(monthValue);
    setError("");
    
    // Fetch current user details`,
    `const row = selected[0];
    const period = parseMonth(monthValue);
    setError("");
    
    // Fetch current user details`
);

// We need to re-add setShowForm(true) to openDeductionForm
content = content.replace(
    `deductionYear: period.year,
    }));
  };`,
    `deductionYear: period.year,
    }));
    setShowForm(true);
  };`
);

fs.writeFileSync(path, content);
console.log("Reverted layout");

