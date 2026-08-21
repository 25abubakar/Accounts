const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace("await loadReport();", "await loadRows(period.year, period.month);");
content = content.replace("await loadReport();", "await loadRows(period.year, period.month);");

// Inject the custom modal JSX right before the adjustmentForm modal
const customModal = `
      {showApprovalModal && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-900/40 backdrop-blur-sm">
          <div className="w-[350px] overflow-hidden rounded-xl bg-white shadow-2xl">
            <div className="bg-green-500 px-4 py-3 text-white">
              <h2 className="text-sm font-bold flex items-center gap-2"><Check size={16}/> Verify Adjustment Approval</h2>
            </div>
            <div className="p-5 space-y-4">
              <p className="text-sm text-slate-700">Enter your PIN code to approve the adjustment of <span className="font-bold">Rs. {approvalRow?.adjustmentAmount}</span> for <span className="font-bold">{approvalRow?.employeeName}</span>.</p>
              <label className="block text-xs font-bold text-slate-700">
                PIN Code
                <input 
                  type="password"
                  className="mt-1 h-10 w-full rounded border border-slate-300 px-3 text-sm focus:border-green-500 focus:ring-1 focus:ring-green-500"
                  value={pinCode}
                  onChange={e => setPinCode(e.target.value)}
                  placeholder="e.g. 123456"
                  autoFocus
                />
              </label>
              <div className="flex justify-end gap-2 pt-2">
                <button type="button" onClick={() => setShowApprovalModal(false)} className="rounded bg-slate-100 px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-200">Cancel</button>
                <button type="button" disabled={!pinCode} onClick={handleApproveSubmit} className="rounded flex items-center gap-2 bg-green-500 px-4 py-2 text-xs font-bold text-white hover:bg-green-600 disabled:opacity-50">Approve</button>
              </div>
            </div>
          </div>
        </div>
      )}
`;

content = content.replace("{adjustmentForm?.visible && (", customModal + "\n      {adjustmentForm?.visible && (");

fs.writeFileSync(path, content, "utf8");
console.log("Done");

