const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

// Add state for Adjustment Form
content = content.replace(
    `const [saving, setSaving] = useState(false);`,
    `const [saving, setSaving] = useState(false);
  const [adjustmentForm, setAdjustmentForm] = useState<{ visible: boolean; personId: string; employeeName: string; amount: number; remarks: string } | null>(null);`
);

// Add Adjustment form submit handler
content = content.replace(
    `const toggleOvertimeApproval = async (row: DeductionGridRow) => {`,
    `const saveAdjustment = async () => {
    if (!adjustmentForm) return;
    try {
      setSaving(true);
      const period = parseMonth(monthValue);
      await dailyAttendanceApi.saveAdjustment(adjustmentForm.personId, period.year, period.month, adjustmentForm.amount, adjustmentForm.remarks);
      setAdjustmentForm(null);
      await loadReport();
    } catch (e) {
      window.alert("Failed to save adjustment: " + getApiErrorMessage(e));
    } finally {
      setSaving(false);
    }
  };

  const toggleOvertimeApproval = async (row: DeductionGridRow) => {`
);

// Add Adjustment Column to Grid and onCellDblClick
content = content.replace(
    `allowColumnResizing`,
    `allowColumnResizing
            onCellDblClick={(e) => {
              if (e.column.dataField === "adjustmentAmount" && e.data) {
                if (!canAddDeduction) return;
                setAdjustmentForm({
                  visible: true,
                  personId: e.data.personId,
                  employeeName: e.data.employeeName,
                  amount: e.data.adjustmentAmount ?? 0,
                  remarks: ""
                });
              }
            }}`
);

// Render the Adjustment Column
content = content.replace(
    `{/* Final Pay */}`,
    `<Column dataField="adjustmentAmount" caption="ADJUSTMENT" width={110} alignment="right" cellRender={({ value }) => <span className="font-bold text-indigo-600">{money.format(value ?? 0)}</span>}/>
            
            {/* Final Pay */}`
);

// Render the Adjustment Form Popup
content = content.replace(
    `{dialogs}`,
    `{dialogs}

      {adjustmentForm?.visible && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-900/40 backdrop-blur-sm">
          <div className="w-[400px] overflow-hidden rounded-2xl bg-white shadow-2xl">
            <div className="bg-indigo-500 px-4 py-3 text-white">
              <h2 className="text-sm font-black">Adjustment: {adjustmentForm.employeeName}</h2>
            </div>
            <div className="p-5 space-y-4">
              <label className="block text-xs font-bold text-slate-700">
                Amount (use - for deduction, + for addition)
                <input 
                  type="number"
                  className="mt-1 h-10 w-full rounded border border-slate-300 px-3 text-sm font-bold text-slate-900 focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                  value={adjustmentForm.amount}
                  onChange={e => setAdjustmentForm({ ...adjustmentForm, amount: Number(e.target.value) })}
                />
              </label>
              <label className="block text-xs font-bold text-slate-700">
                Remarks
                <input 
                  type="text"
                  className="mt-1 h-10 w-full rounded border border-slate-300 px-3 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
                  value={adjustmentForm.remarks}
                  onChange={e => setAdjustmentForm({ ...adjustmentForm, remarks: e.target.value })}
                />
              </label>
              <div className="flex justify-end gap-2 pt-2">
                <button type="button" onClick={() => setAdjustmentForm(null)} className="rounded bg-slate-100 px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200">Cancel</button>
                <button type="button" disabled={saving} onClick={saveAdjustment} className="rounded bg-indigo-500 px-4 py-2 text-xs font-bold text-white hover:bg-indigo-600">Save</button>
              </div>
            </div>
          </div>
        </div>
      )}`
);

fs.writeFileSync(path, content);
console.log("Updated DeductionPage");

