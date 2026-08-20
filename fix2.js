const fs = require('fs');
let code = fs.readFileSync('Frontend/Frontend-Accounts/src/components/NoteFormDrawer.tsx', 'utf8');

// The file has duplicate priorityBar and priorityBadge definitions.
// Let's replace the whole section starting from unction priorityBar up to unction UserNotificationsView
code = code.replace(/function priorityBar[\s\S]*?\/\/ USER view[\s\S]*?function UserNotificationsView/, 
unction priorityBar(code?: string) {
  switch (code?.toUpperCase()) {
    case "CRITICAL": return "bg-red-500";
    case "HIGH":     return "bg-amber-500";
    default:         return "bg-blue-500";
  }
}

function priorityBadge(code?: string) {
  switch (code?.toUpperCase()) {
    case "CRITICAL": return "bg-red-50 text-red-700 border-red-200";
    case "HIGH":     return "bg-amber-50 text-amber-700 border-amber-200";
    default:         return "bg-blue-50 text-blue-700 border-blue-200";
  }
}

// -----------------------------------------------------------------------------
// USER view — highly readable list of admin instructions
// -----------------------------------------------------------------------------
function UserNotificationsView);

fs.writeFileSync('Frontend/Frontend-Accounts/src/components/NoteFormDrawer.tsx', code);
