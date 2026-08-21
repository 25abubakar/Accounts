const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    `import { FEATURE } from "../../lib/featureKeys";`,
    `import { FEATURE } from "../../lib/featureKeys";\nimport { useAuthStore } from "../../store/authStore";`
);

fs.writeFileSync(path, content);
console.log("Fixed import");

