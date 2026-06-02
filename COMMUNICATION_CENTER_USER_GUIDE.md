# Communication Center - User Guide (Facebook/Messenger Style)

## 🎯 کیسے کام کرتا ہے؟

**بالکل Facebook/Messenger کی طرح:**
- ✅ ہر user اپنا data دیکھ سکتا ہے (personal notes)
- ✅ Admin instructions بھیج سکتا ہے (single person یا group کو)
- ✅ Group instructions سب کو نظر آتی ہیں
- ✅ Private messages صرف receiver کو نظر آتی ہیں
- ✅ Menu-specific notes صرف اس menu پر نظر آتی ہیں

---

## 📋 Note Types (Visibility Rules)

### **1. GENERAL - سب کو نظر آئے**
```typescript
// Admin creates general announcement
POST /api/app-notes
{
  "title": "System Maintenance",
  "noteBody": "System will be down on Sunday",
  "noteTypeCode": "ANNOUNCEMENT",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "GENERAL",  // ← سب کو نظر آئے گی
  "priorityCode": "HIGH",
  "isPublished": true,
  "targets": []  // Empty = all users
}

// Result: سب users کو نظر آئے گی
```

### **2. USER - صرف ایک specific user کو**
```typescript
// Admin sends instruction to single person (like Facebook private message)
POST /api/app-notes
{
  "title": "Your Task",
  "noteBody": "Please complete the report by Friday",
  "noteTypeCode": "INSTRUCTION",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "USER",  // ← صرف specific user کو
  "priorityCode": "HIGH",
  "isPublished": true,
  "targets": [
    {
      "targetTypeCode": "USER",
      "targetValue": "user123@example.com"  // ← صرف اس user کو نظر آئے گی
    }
  ]
}

// Result: صرف user123@example.com کو نظر آئے گی
```

### **3. ROLE - Group کو (like Facebook group message)**
```typescript
// Admin sends instruction to all Managers (like group message)
POST /api/app-notes
{
  "title": "Manager Meeting",
  "noteBody": "All managers please attend meeting on Monday",
  "noteTypeCode": "INSTRUCTION",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "ROLE",  // ← specific role کو
  "priorityCode": "HIGH",
  "isPublished": true,
  "targets": [
    {
      "targetTypeCode": "ROLE",
      "targetValue": "Manager"  // ← سب Managers کو نظر آئے گی
    }
  ]
}

// Result: سب Managers کو نظر آئے گی
```

### **4. PRIVATE - صرف creator کو (personal note)**
```typescript
// User creates personal note (like Facebook personal note)
POST /api/app-notes
{
  "title": "My Todo",
  "noteBody": "Remember to call client tomorrow",
  "noteTypeCode": "REMINDER",
  "sourceTypeCode": "USER",  // ← User created
  "visibilityTypeCode": "PRIVATE",  // ← صرف creator کو نظر آئے
  "priorityCode": "NORMAL",
  "isPublished": true,
  "targets": []
}

// Result: صرف creator کو نظر آئے گی
```

### **5. MENU - صرف specific page پر**
```typescript
// Admin creates note for specific menu (like page-specific help)
POST /api/app-notes
{
  "title": "How to use this page",
  "noteBody": "Click the + button to add new employee",
  "noteTypeCode": "HELP",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "MENU",  // ← specific menu پر
  "menuCode": "EMPLOYEES",  // ← صرف Employees page پر نظر آئے گی
  "priorityCode": "LOW",
  "isPublished": true,
  "targets": []
}

// Result: صرف Employees page پر نظر آئے گی
```

### **6. RECORD - صرف specific record پر**
```typescript
// Admin creates note for specific record (like comment on post)
POST /api/app-notes
{
  "title": "Review Required",
  "noteBody": "Please review this employee's performance",
  "noteTypeCode": "COMMENT",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "RECORD",  // ← specific record پر
  "entityType": "Employee",
  "entityId": "emp-123",  // ← صرف اس employee کی detail page پر
  "priorityCode": "NORMAL",
  "isPublished": true,
  "targets": []
}

// Result: صرف emp-123 کی detail page پر نظر آئے گی
```

---

## 🎯 Use Cases (Facebook/Messenger Style)

### **Use Case 1: Admin Announcement (سب کو)**
```typescript
// Admin creates announcement - سب کو نظر آئے
POST /api/app-notes
{
  "title": "Holiday Notice",
  "noteBody": "Office will be closed on Monday",
  "noteTypeCode": "ANNOUNCEMENT",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "GENERAL",
  "priorityCode": "HIGH",
  "isPublished": true,
  "isPinned": true,  // Pin to top
  "targets": []
}

// All users see this in their dashboard
```

### **Use Case 2: Private Message (صرف ایک user کو)**
```typescript
// Admin sends private instruction - صرف ایک user کو
POST /api/app-notes
{
  "title": "Your Performance Review",
  "noteBody": "Please schedule a meeting with HR",
  "noteTypeCode": "INSTRUCTION",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "USER",
  "priorityCode": "HIGH",
  "requireAcknowledgement": true,  // User must acknowledge
  "targets": [
    { "targetTypeCode": "USER", "targetValue": "john@example.com" }
  ]
}

// Only john@example.com sees this
```

### **Use Case 3: Group Message (department کو)**
```typescript
// Admin sends to all IT department - سب IT staff کو
POST /api/app-notes
{
  "title": "IT Department Meeting",
  "noteBody": "All IT staff please attend meeting on Friday",
  "noteTypeCode": "INSTRUCTION",
  "sourceTypeCode": "ADMIN",
  "visibilityTypeCode": "ROLE",
  "priorityCode": "HIGH",
  "targets": [
    { "targetTypeCode": "ROLE", "targetValue": "IT Staff" }
  ]
}

// All IT staff see this
```

### **Use Case 4: Personal Note (صرف اپنے لیے)**
```typescript
// User creates personal reminder - صرف اپنے لیے
POST /api/app-notes
{
  "title": "My Todo",
  "noteBody": "Call client at 3 PM",
  "noteTypeCode": "REMINDER",
  "sourceTypeCode": "USER",
  "visibilityTypeCode": "PRIVATE",
  "priorityCode": "NORMAL",
  "targets": []
}

// Only creator sees this
```

---

## 🔐 Backend Filtering (Already Implemented)

### **AppNoteService.IsVisible() Method**
```csharp
private static bool IsVisible(
    AppNote note, string userId, IList<string> roles,
    string? menuCode, string? entityType, string? entityId)
{
    // User notes: only visible to creator
    if (note.SourceTypeCode == "USER" && note.CreatedBy != userId)
        return false;  // ← صرف creator کو نظر آئے

    return note.VisibilityTypeCode switch
    {
        "GENERAL"  => true,  // ← سب کو نظر آئے
        "ALL_USERS" => string.IsNullOrEmpty(note.MenuCode) && string.IsNullOrEmpty(note.EntityType),
        "MENU"     => string.Equals(note.MenuCode, menuCode, StringComparison.OrdinalIgnoreCase),  // ← صرف اس menu پر
        "RECORD"   => SameRecord(note, entityType, entityId),  // ← صرف اس record پر
        "PRIVATE"  => note.CreatedBy == userId,  // ← صرف creator کو
        "USER"     => note.Targets.Any(t => t.IsActive && t.TargetTypeCode == "USER" && t.TargetValue == userId),  // ← صرف target user کو
        "ROLE"     => note.Targets.Any(t => t.IsActive && t.TargetTypeCode == "ROLE" && roles.Contains(t.TargetValue)),  // ← صرف target role کو
        _          => true
    };
}
```

**یہ بالکل Facebook/Messenger کی طرح کام کرتا ہے!**

---

## 📱 Frontend Implementation

### **1. Fetch User's Notes**
```typescript
// Get all notes visible to current user
const fetchMyNotes = async () => {
  try {
    const response = await axios.get('/api/app-notes/visible');
    // Returns only notes visible to current user
    return response.data.data;
  } catch (error) {
    console.error('Failed to fetch notes', error);
    return [];
  }
};
```

### **2. Fetch Notes for Specific Menu**
```typescript
// Get notes for specific page (like Employees page)
const fetchMenuNotes = async (menuCode: string) => {
  try {
    const response = await axios.get(`/api/app-notes/visible?menuCode=${menuCode}`);
    // Returns notes for this menu + general notes
    return response.data.data;
  } catch (error) {
    console.error('Failed to fetch menu notes', error);
    return [];
  }
};

// Usage in Employees page
useEffect(() => {
  fetchMenuNotes('EMPLOYEES').then(setNotes);
}, []);
```

### **3. Fetch Notes for Specific Record**
```typescript
// Get notes for specific record (like employee detail page)
const fetchRecordNotes = async (entityType: string, entityId: string) => {
  try {
    const response = await axios.get(
      `/api/app-notes/visible?entityType=${entityType}&entityId=${entityId}`
    );
    // Returns notes for this record + general notes
    return response.data.data;
  } catch (error) {
    console.error('Failed to fetch record notes', error);
    return [];
  }
};

// Usage in Employee Detail page
useEffect(() => {
  fetchRecordNotes('Employee', employeeId).then(setNotes);
}, [employeeId]);
```

### **4. Create Personal Note**
```typescript
// User creates personal note (like Facebook personal note)
const createPersonalNote = async (title: string, body: string) => {
  try {
    const response = await axios.post('/api/app-notes', {
      title,
      noteBody: body,
      noteTypeCode: 'REMINDER',
      sourceTypeCode: 'USER',  // ← User created
      visibilityTypeCode: 'PRIVATE',  // ← Only creator sees
      priorityCode: 'NORMAL',
      isPublished: true,
      targets: []
    });
    return response.data.data;
  } catch (error) {
    console.error('Failed to create note', error);
    throw error;
  }
};
```

### **5. Admin Creates Instruction**
```typescript
// Admin creates instruction for single user
const createUserInstruction = async (
  title: string, 
  body: string, 
  targetUserId: string
) => {
  try {
    const response = await axios.post('/api/app-notes', {
      title,
      noteBody: body,
      noteTypeCode: 'INSTRUCTION',
      sourceTypeCode: 'ADMIN',
      visibilityTypeCode: 'USER',  // ← Single user
      priorityCode: 'HIGH',
      requireAcknowledgement: true,
      isPublished: true,
      targets: [
        {
          targetTypeCode: 'USER',
          targetValue: targetUserId  // ← Target user email
        }
      ]
    });
    return response.data.data;
  } catch (error) {
    console.error('Failed to create instruction', error);
    throw error;
  }
};

// Admin creates instruction for group
const createGroupInstruction = async (
  title: string, 
  body: string, 
  targetRole: string
) => {
  try {
    const response = await axios.post('/api/app-notes', {
      title,
      noteBody: body,
      noteTypeCode: 'INSTRUCTION',
      sourceTypeCode: 'ADMIN',
      visibilityTypeCode: 'ROLE',  // ← Group
      priorityCode: 'HIGH',
      isPublished: true,
      targets: [
        {
          targetTypeCode: 'ROLE',
          targetValue: targetRole  // ← Target role (Manager, IT Staff, etc.)
        }
      ]
    });
    return response.data.data;
  } catch (error) {
    console.error('Failed to create instruction', error);
    throw error;
  }
};
```

### **6. Notification Bell**
```typescript
// Get unread count for notification bell
const fetchUnreadCount = async () => {
  try {
    const response = await axios.get('/api/app-notes/unread-count');
    return response.data.data;  // Number of unread admin instructions
  } catch (error) {
    console.error('Failed to fetch unread count', error);
    return 0;
  }
};

// Usage in header
const [unreadCount, setUnreadCount] = useState(0);

useEffect(() => {
  fetchUnreadCount().then(setUnreadCount);
  
  // Poll every 30 seconds
  const interval = setInterval(() => {
    fetchUnreadCount().then(setUnreadCount);
  }, 30000);
  
  return () => clearInterval(interval);
}, []);

// Show notification bell
<Badge count={unreadCount}>
  <BellIcon />
</Badge>
```

---

## 🎨 Frontend UI Components

### **1. Dashboard - Show All Notes**
```typescript
const DashboardPage = () => {
  const [notes, setNotes] = useState([]);

  useEffect(() => {
    // Fetch all notes visible to current user
    fetchMyNotes().then(setNotes);
  }, []);

  return (
    <div>
      <h1>My Dashboard</h1>
      
      {/* Admin Instructions */}
      <section>
        <h2>Instructions</h2>
        {notes
          .filter(n => n.sourceTypeCode === 'ADMIN')
          .map(note => (
            <NoteCard key={note.noteId} note={note} />
          ))}
      </section>

      {/* Personal Notes */}
      <section>
        <h2>My Notes</h2>
        {notes
          .filter(n => n.sourceTypeCode === 'USER')
          .map(note => (
            <NoteCard key={note.noteId} note={note} />
          ))}
      </section>
    </div>
  );
};
```

### **2. Employees Page - Show Menu Notes**
```typescript
const EmployeesPage = () => {
  const [notes, setNotes] = useState([]);

  useEffect(() => {
    // Fetch notes for Employees menu
    fetchMenuNotes('EMPLOYEES').then(setNotes);
  }, []);

  return (
    <div>
      <h1>Employees</h1>
      
      {/* Show menu-specific notes */}
      {notes.length > 0 && (
        <Alert>
          {notes.map(note => (
            <div key={note.noteId}>
              <strong>{note.title}</strong>
              <p>{note.noteBody}</p>
            </div>
          ))}
        </Alert>
      )}

      {/* Employee list */}
      <EmployeeList />
    </div>
  );
};
```

### **3. Employee Detail - Show Record Notes**
```typescript
const EmployeeDetailPage = ({ employeeId }) => {
  const [notes, setNotes] = useState([]);

  useEffect(() => {
    // Fetch notes for this employee record
    fetchRecordNotes('Employee', employeeId).then(setNotes);
  }, [employeeId]);

  return (
    <div>
      <h1>Employee Details</h1>
      
      {/* Show record-specific notes */}
      {notes.length > 0 && (
        <section>
          <h2>Comments</h2>
          {notes.map(note => (
            <CommentCard key={note.noteId} note={note} />
          ))}
        </section>
      )}

      {/* Employee details */}
      <EmployeeDetails employeeId={employeeId} />
    </div>
  );
};
```

### **4. Create Note Form**
```typescript
const CreateNoteForm = () => {
  const [formData, setFormData] = useState({
    title: '',
    noteBody: '',
    noteTypeCode: 'REMINDER',
    visibilityTypeCode: 'PRIVATE',
    priorityCode: 'NORMAL',
    targets: []
  });

  const handleSubmit = async (e) => {
    e.preventDefault();
    
    try {
      await axios.post('/api/app-notes', {
        ...formData,
        sourceTypeCode: 'USER',  // User created
        isPublished: true
      });
      
      toast.success('Note created successfully');
      // Refresh notes list
    } catch (error) {
      toast.error('Failed to create note');
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <input
        type="text"
        placeholder="Title"
        value={formData.title}
        onChange={e => setFormData({...formData, title: e.target.value})}
      />
      
      <textarea
        placeholder="Note body"
        value={formData.noteBody}
        onChange={e => setFormData({...formData, noteBody: e.target.value})}
      />
      
      <select
        value={formData.priorityCode}
        onChange={e => setFormData({...formData, priorityCode: e.target.value})}
      >
        <option value="LOW">Low</option>
        <option value="NORMAL">Normal</option>
        <option value="HIGH">High</option>
        <option value="CRITICAL">Critical</option>
      </select>
      
      <button type="submit">Create Note</button>
    </form>
  );
};
```

### **5. Admin Instruction Form**
```typescript
const AdminInstructionForm = () => {
  const [formData, setFormData] = useState({
    title: '',
    noteBody: '',
    visibilityTypeCode: 'GENERAL',  // Default: all users
    priorityCode: 'NORMAL',
    targetType: 'ALL',  // ALL, USER, ROLE
    targetValue: ''
  });

  const handleSubmit = async (e) => {
    e.preventDefault();
    
    const targets = [];
    if (formData.targetType === 'USER') {
      targets.push({
        targetTypeCode: 'USER',
        targetValue: formData.targetValue  // User email
      });
    } else if (formData.targetType === 'ROLE') {
      targets.push({
        targetTypeCode: 'ROLE',
        targetValue: formData.targetValue  // Role name
      });
    }
    
    try {
      await axios.post('/api/app-notes', {
        title: formData.title,
        noteBody: formData.noteBody,
        noteTypeCode: 'INSTRUCTION',
        sourceTypeCode: 'ADMIN',
        visibilityTypeCode: formData.targetType === 'ALL' ? 'GENERAL' : 
                           formData.targetType === 'USER' ? 'USER' : 'ROLE',
        priorityCode: formData.priorityCode,
        requireAcknowledgement: true,
        isPublished: true,
        targets
      });
      
      toast.success('Instruction sent successfully');
    } catch (error) {
      toast.error('Failed to send instruction');
    }
  };

  return (
    <form onSubmit={handleSubmit}>
      <input
        type="text"
        placeholder="Title"
        value={formData.title}
        onChange={e => setFormData({...formData, title: e.target.value})}
      />
      
      <textarea
        placeholder="Instruction body"
        value={formData.noteBody}
        onChange={e => setFormData({...formData, noteBody: e.target.value})}
      />
      
      <select
        value={formData.targetType}
        onChange={e => setFormData({...formData, targetType: e.target.value})}
      >
        <option value="ALL">All Users</option>
        <option value="USER">Single User</option>
        <option value="ROLE">Group (Role)</option>
      </select>
      
      {formData.targetType === 'USER' && (
        <input
          type="email"
          placeholder="User email"
          value={formData.targetValue}
          onChange={e => setFormData({...formData, targetValue: e.target.value})}
        />
      )}
      
      {formData.targetType === 'ROLE' && (
        <select
          value={formData.targetValue}
          onChange={e => setFormData({...formData, targetValue: e.target.value})}
        >
          <option value="">Select Role</option>
          <option value="Manager">Manager</option>
          <option value="Supervisor">Supervisor</option>
          <option value="Employee">Employee</option>
        </select>
      )}
      
      <select
        value={formData.priorityCode}
        onChange={e => setFormData({...formData, priorityCode: e.target.value})}
      >
        <option value="LOW">Low</option>
        <option value="NORMAL">Normal</option>
        <option value="HIGH">High</option>
        <option value="CRITICAL">Critical</option>
      </select>
      
      <button type="submit">Send Instruction</button>
    </form>
  );
};
```

---

## ✅ Summary

### **Backend - Already Working!**
- ✅ Lookups accessible to all authenticated users (no permission check)
- ✅ Notes filtered by visibility rules (GENERAL, USER, ROLE, PRIVATE, MENU, RECORD)
- ✅ User notes only visible to creator
- ✅ Admin instructions visible based on targets
- ✅ Group instructions visible to all users in that role

### **Frontend - What You Need**
1. **Dashboard** - Show all notes visible to current user
2. **Personal Notes** - User can create/view their own notes
3. **Admin Instructions** - Admin can send to single user or group
4. **Menu Notes** - Show notes on specific pages
5. **Record Notes** - Show notes on specific records
6. **Notification Bell** - Show unread count

### **کیسے کام کرتا ہے؟**
- ✅ ہر user login کرے → اپنے notes دیکھے (personal + admin instructions)
- ✅ Admin instruction بھیجے → single user یا group کو
- ✅ Group instruction → سب users کو نظر آئے (جو اس role میں ہیں)
- ✅ Private note → صرف creator کو نظر آئے
- ✅ بالکل Facebook/Messenger کی طرح!

**Status: Backend ✅ READY | Frontend needs implementation**
