const fs = require("fs");
const content = `// src/api/staffApi.ts
import api from "./axios";
import type { StaffDto, UpdateStaffDto, TransferStaffDto } from "../types";

export const staffApi = {
  // GET /api/employees
  getAll: async (): Promise<StaffDto[]> => {
    const response = await api.get<StaffDto[]>("/api/employees");
    return response.data;
  },

  // GET /api/employees/{id}
  getById: async (id: string): Promise<StaffDto> => {
    const response = await api.get<StaffDto>(\`/api/employees/\${id}\`);
    return response.data;
  },

  getByLogin: async (loginOrEmail: string): Promise<StaffDto | null> => {
    try {
      const response = await api.get<StaffDto>(\`/api/employees/by-login/\${encodeURIComponent(loginOrEmail)}\`);
      return response.data;
    } catch {
      return null;
    }
  },

  // POST /api/employees/hire/{vacancyId}
  hire: async (vacancyId: string, data: { fullName: string; email?: string; phone?: string }): Promise<StaffDto> => {
    const response = await api.post<StaffDto>(\`/api/employees/hire/\${vacancyId}\`, data);
    return response.data;
  },

  // POST /api/employees/hire-person/{vacancyId}?personId=
  hireRegisteredPerson: async (vacancyId: string, personId: string): Promise<StaffDto & { staffId: string }> => {
    const response = await api.post<StaffDto & { staffId: string }>(
      \`/api/employees/hire-person/\${vacancyId}\`, 
      null, 
      { params: { personId } }
    );
    return response.data;
  },

  // PUT /api/employees/{id}
  update: async (id: string, data: UpdateStaffDto): Promise<StaffDto> => {
    const response = await api.put<StaffDto>(\`/api/employees/\${id}\`, data);
    return response.data;
  },

  // DELETE /api/employees/{id}
  fire: async (id: string, reason?: string): Promise<void> => {
    await api.post(\`/api/employees/\${id}/terminate\`, { reason: reason?.trim() || null });
  },

  retire: async (id: string, reason?: string): Promise<void> => {
    await api.post(\`/api/employees/\${id}/retire\`, { reason: reason?.trim() || null });
  },

  // GET /api/employees/search?q=
  search: async (query: string): Promise<StaffDto[]> => {
    const response = await api.get<StaffDto[]>("/api/employees/search", {
      params: { q: query },
    });
    return response.data;
  },

  // POST /api/employees/{id}/upload-photo
  uploadPhoto: async (id: string, file: File): Promise<{ photoUrl: string }> => {
    const formData = new FormData();
    formData.append("photo", file);
    const response = await api.post<{ photoUrl: string }>(
      \`/api/employees/\${id}/upload-photo\`,
      formData,
      { headers: { "Content-Type": "multipart/form-data" } }
    );
    return response.data;
  },

  // DELETE /api/employees/{id}/photo
  deletePhoto: async (id: string): Promise<void> => {
    await api.delete(\`/api/employees/\${id}/photo\`);
  },

  // PUT /api/employees/{id}/transfer
  transfer: async (id: string, data: TransferStaffDto): Promise<StaffDto> => {
    const response = await api.put<StaffDto>(\`/api/employees/\${id}/transfer\`, data);
    return response.data;
  },
};
`;
fs.writeFileSync("Frontend/Frontend-Accounts/src/api/staffApi.ts", content);
console.log("Fixed");

