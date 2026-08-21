const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
  "const [adjustmentForm, setAdjustmentForm] = useState<{ visible: boolean; personId: string; employeeName: string; amount: number; remarks: string } | null>(null);",
  "const [adjustmentForm, setAdjustmentForm] = useState<{ visible: boolean; personId: string; employeeName: string; amount: number; remarks: string } | null>(null);\n  const [showApprovalModal, setShowApprovalModal] = useState(false);\n  const [approvalRow, setApprovalRow] = useState<any>(null);\n  const [pinCode, setPinCode] = useState<string>(\"\");"
);

const saveAdjustmentRegex = /(const saveAdjustment = async \(\) => \{.*?\n    \};)/s;
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
      await loadReport();
    } catch (e: any) {
      window.alert(e?.response?.data?.message || "Failed to approve adjustment.");
    }
  };
`;
content = content.replace(saveAdjustmentRegex, `$1\n\n${approveFunction}`);

const actionColumnRegex = /cell: \(\{ row \} : \{ row: any \}\) => \(\s*<div className="flex items-center gap-2">\s*\{canAddDeduction && \(\s*<button/;
const actionColumnNew = `cell: ({ row } : { row: any }) => (
            <div className="flex items-center gap-2">
              {(row.original.adjustmentAmount !== 0 && !row.original.isAdjustmentApproved) && (
                <button
                  type="button"
                  title="Approve Adjustment"
                  className="grid h-6 w-6 place-items-center rounded bg-green-500 text-white shadow-sm transition hover:bg-green-600"
                  onClick={() => {
                    setApprovalRow(row.original);
                    setShowApprovalModal(true);
                  }}
                >
                  <Check size={14} />
                </button>
              )}
              {canAddDeduction && (
                <button`;
content = content.replace(actionColumnRegex, actionColumnNew);

content = content.replace(
  "onDoubleClick={(e) => {",
  "onDoubleClick={(e) => {\n                if (e.data?.isAdjustmentApproved) {\n                  window.alert(\"Approved adjustment cannot be modified.\");\n                  return;\n                }"
);

const modalJsx = `
      <Modal 
        isOpen={showApprovalModal} 
        onClose={() => setShowApprovalModal(false)}
        title="Verify Adjustment Approval"
        width="sm"
      >
        <div className="space-y-4">
          <div className="rounded-lg border border-emerald-100 bg-emerald-50/50 p-4">
            <div className="text-center space-y-2">
              <div className="mx-auto grid h-12 w-12 place-items-center rounded-full bg-white shadow-sm">
                <Check className="text-green-500" size={24} />
              </div>
              <p className="text-sm text-slate-600">
                Enter your PIN code to approve the adjustment of <span className="font-bold text-slate-900">Rs. {approvalRow?.adjustmentAmount}</span> for <span className="font-bold text-slate-900">{approvalRow?.employeeName}</span>.
              </p>
            </div>
          </div>
          <div>
            <label className="mb-1.5 block text-xs font-semibold text-slate-700">PIN Code</label>
            <input
              type="password"
              className="w-full rounded-lg border-0 px-3 py-2 text-sm text-slate-700 shadow-sm ring-1 ring-inset ring-slate-200 focus:ring-2 focus:ring-inset focus:ring-emerald-500"
              value={pinCode}
              onChange={(e) => setPinCode(e.target.value)}
              placeholder="Enter PIN (e.g. 123456)"
              autoFocus
            />
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <button
              onClick={() => setShowApprovalModal(false)}
              className="rounded-lg px-4 py-2 text-sm font-semibold text-slate-700 transition hover:bg-slate-100"
            >
              Cancel
            </button>
            <button
              onClick={handleApproveSubmit}
              disabled={!pinCode}
              className="flex items-center gap-2 rounded-lg bg-green-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-green-700 disabled:opacity-50"
            >
              <Check size={16} /> Approve
            </button>
          </div>
        </div>
      </Modal>
    </div>
`;
content = content.replace(/<\/div>\s*<Modal isOpen=\{showForm\}/, modalJsx + "\n      <Modal isOpen={showForm}");
fs.writeFileSync(path, content, "utf8");
console.log("Done");

