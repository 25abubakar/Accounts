import {createRoot} from '../../../Frontend/Frontend-Accounts/node_modules/react-dom/client';
import {MemoryRouter} from '../../../Frontend/Frontend-Accounts/node_modules/react-router-dom';
import SchedulerWorkspacePage from '../../../Frontend/Frontend-Accounts/src/pages/scheduler/SchedulerWorkspacePage';
import '../../../Frontend/Frontend-Accounts/src/index.css';
createRoot(document.getElementById('root')!).render(<MemoryRouter initialEntries={['/scheduler/jobs']}><SchedulerWorkspacePage/></MemoryRouter>);
