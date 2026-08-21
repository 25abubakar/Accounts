const fs = require("fs");
const path = "Frontend/Frontend-Accounts/src/pages/attendance/DeductionPage.tsx";
let content = fs.readFileSync(path, "utf8");

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
console.log("Fixed setShowForm(true) again");

