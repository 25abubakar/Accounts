const fs = require("fs");
const path = "Accounts/Services/Services/AppNoteService.cs";
let content = fs.readFileSync(path, "utf8");

content = content.replace(
    `        public async Task<List<AppNoteDto>> GetVisibleAsync(
            string staffId,
            string identityUserId,
            string? menuCode,
            string? entityType,
            string? entityId,
            CancellationToken ct)
        {
            var now = PakistanClock.Now();`,
    `        public async Task<List<AppNoteDto>> GetVisibleAsync(
            string staffId,
            string identityUserId,
            string? menuCode,
            string? entityType,
            string? entityId,
            CancellationToken ct)
        {
            try
            {
                var now = PakistanClock.Now();`
);

content = content.replace(
    `            // Step 4: exclude dismissed, map to DTOs
            return notes
                .Where(n => !stateMap.TryGetValue(n.NoteId, out var st) || !st.IsDismissed)
                .Select(n =>
                {
                    stateMap.TryGetValue(n.NoteId, out var state);
                    return ToDto(n, state);
                })
                .ToList();
        }`,
    `            // Step 4: exclude dismissed, map to DTOs
            return notes
                .Where(n => !stateMap.TryGetValue(n.NoteId, out var st) || !st.IsDismissed)
                .Select(n =>
                {
                    stateMap.TryGetValue(n.NoteId, out var state);
                    return ToDto(n, state);
                })
                .ToList();
            }
            catch (OperationCanceledException)
            {
                return new List<AppNoteDto>();
            }
        }`
);

fs.writeFileSync(path, content);
console.log("Success");

