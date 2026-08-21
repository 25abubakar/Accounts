const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

const oldCol = `<Column dataField="adjustmentAmount" caption="ADJUSTMENT" width={110} alignment="right" cellRender={({ value }) => <span className="font-bold text-indigo-600">{money.format(value ?? 0)}</span>}/>`;
const newCol = `<Column dataField="adjustmentAmount" caption="ADJUSTMENT" width={120} alignment="right" cellRender={({ data }) => (
                <div className="flex items-center justify-end gap-2">
                  {(data.adjustmentAmount !== 0 && !data.isAdjustmentApproved) && (
                    <button
                      title="Approve Adjustment"
                      className="grid h-5 w-5 place-items-center rounded bg-green-500 text-white shadow hover:bg-green-600"
                      onClick={(e) => {
                        e.stopPropagation();
                        setApprovalRow(data);
                        setShowApprovalModal(true);
                      }}
                    >
                      <Check size={12} strokeWidth={3} />
                    </button>
                  )}
                  <span className="font-bold text-indigo-600">{money.format(data.adjustmentAmount ?? 0)}</span>
                </div>
              )}/>`;

content = content.replace(oldCol, newCol);
fs.writeFileSync(path, content, "utf8");
console.log("Done!");

