const fs = require('fs');
let code = fs.readFileSync('Frontend/Frontend-Accounts/src/components/NoteFormDrawer.tsx', 'utf8');

code = code.replace(/const \{ decrement \} = useNotesStore\(\);[\s\S]*?\/\/ 🌟 DEBUGGING:/, 'const { decrement } = useNotesStore();\n\n  useEffect(() => {\n    if (!isOpen) return;\n    let mounted = true;\n\n    const load = async () => {\n      setIsLoading(true);\n      setError(null);\n      try {\n        const response = await appNotesApi.getVisible();\n        if (!mounted) return;\n\n        // 🌟 DEBUGGING:');

fs.writeFileSync('Frontend/Frontend-Accounts/src/components/NoteFormDrawer.tsx', code);
