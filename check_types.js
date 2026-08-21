const fs = require("fs");
const sql = fs.readFileSync("update_sp_final7.sql", "utf8");

const firstSelectMatch = sql.match(/SELECT\s+CAST\(NULL AS bigint\) AS Id,[\s\S]*?WHERE 1 = 0;/);
const finalSelectMatch = sql.match(/SELECT\s+ROW_NUMBER\(\) OVER[\s\S]*?OPTION \(MAXRECURSION 31\);/);

console.log("FIRST SELECT:");
console.log(firstSelectMatch[0]);

console.log("\n\nSECOND SELECT:");
console.log(finalSelectMatch[0]);

