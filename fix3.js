const fs = require('fs');
let code = fs.readFileSync('Frontend/Frontend-Accounts/src/components/NoteFormDrawer.tsx', 'utf8');

const startIdx = code.indexOf('function priorityBar');
const endIdx = code.indexOf('function UserNotificationsView', startIdx);

if (startIdx !== -1 && endIdx !== -1) {
  const newStr = 'function priorityBar(code?: string) {\n' +
'  switch (code?.toUpperCase()) {\n' +
'    case "CRITICAL": return "bg-red-500";\n' +
'    case "HIGH":     return "bg-amber-500";\n' +
'    default:         return "bg-blue-500";\n' +
'  }\n' +
'}\n\n' +
'function priorityBadge(code?: string) {\n' +
'  switch (code?.toUpperCase()) {\n' +
'    case "CRITICAL": return "bg-red-50 text-red-700 border-red-200";\n' +
'    case "HIGH":     return "bg-amber-50 text-amber-700 border-amber-200";\n' +
'    default:         return "bg-blue-50 text-blue-700 border-blue-200";\n' +
'  }\n' +
'}\n\n' +
'// -----------------------------------------------------------------------------\n' +
'// USER view - highly readable list of admin instructions\n' +
'// -----------------------------------------------------------------------------\n';

  code = code.substring(0, startIdx) + newStr + code.substring(endIdx);
  fs.writeFileSync('Frontend/Frontend-Accounts/src/components/NoteFormDrawer.tsx', code);
}
