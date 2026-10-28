import {defineConfig} from '../../../Frontend/Frontend-Accounts/node_modules/vite/dist/node/index.js';
import react from '../../../Frontend/Frontend-Accounts/node_modules/@vitejs/plugin-react/dist/index.js';
import tailwind from '../../../Frontend/Frontend-Accounts/node_modules/@tailwindcss/vite/dist/index.mjs';
import {fileURLToPath} from 'node:url';
const modules=fileURLToPath(new URL('../../../Frontend/Frontend-Accounts/node_modules/',import.meta.url));
export default defineConfig({
 root:fileURLToPath(new URL('./',import.meta.url)),plugins:[react(),tailwind()],
 resolve:{alias:Object.fromEntries(['react','react-dom','react-router-dom','devextreme','devextreme-react','lucide-react','tailwindcss','xlsx','axios','zustand'].map(name=>[name,modules+name])),dedupe:['react','react-dom']},
 build:{outDir:'obj/ui-bundle',emptyOutDir:false,rollupOptions:{input:fileURLToPath(new URL('ui-fixture.html',import.meta.url))}},
});
