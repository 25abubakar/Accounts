namespace Accounts.DTOs.CommCenter
{
    // ── Response DTO ─────────────────────────────────────────────────────────
    public class AppNoteDto
    {
        public int     NoteId               { get; set; }
        public string  Title                { get; set; } = string.Empty;
        public string  NoteBody             { get; set; } = string.Empty;
        public string  NoteTypeCode         { get; set; } = string.Empty;
        public string  SourceTypeCode       { get; set; } = string.Empty;
        public string? CategoryCode         { get; set; }
        public string  PriorityCode         { get; set; } = string.Empty;
        public string  VisibilityTypeCode   { get; set; } = string.Empty;
        public string? MenuCode             { get; set; }
        public string? ModuleName           { get; set; }
        public string? EntityType           { get; set; }
        public string? EntityId             { get; set; }
        public bool    IsPublished          { get; set; }
        public bool    IsPinned             { get; set; }
        public bool    IsPopup              { get; set; }
        public bool    IsBanner             { get; set; }
        public bool    RequireAcknowledgement { get; set; }
        public bool    AllowDismiss         { get; set; }
        public bool    IsRead               { get; set; }
        public bool    IsAcknowledged       { get; set; }
        public bool    IsDismissed          { get; set; }
        public bool    IsReadOnly           { get; set; }
        public string? CreatedBy            { get; set; }
        public DateTime CreatedOnUtc        { get; set; }
        public DateTime? StartDateUtc       { get; set; }
        public DateTime? EndDateUtc         { get; set; }
    }

    // ── Target DTO ───────────────────────────────────────────────────────────
    public class AppNoteTargetRequest
    {
        public string TargetTypeCode { get; set; } = string.Empty;
        public string TargetValue    { get; set; } = string.Empty;
    }

    // ── Create / Update Request ───────────────────────────────────────────────
    public class CreateAppNoteRequest
    {
        public string  Title                { get; set; } = string.Empty;
        public string  NoteBody             { get; set; } = string.Empty;
        public string  NoteTypeCode         { get; set; } = string.Empty;
        public string  SourceTypeCode       { get; set; } = string.Empty;
        public string? CategoryCode         { get; set; }
        public string  PriorityCode         { get; set; } = string.Empty;
        public string  VisibilityTypeCode   { get; set; } = string.Empty;
        public string? MenuCode             { get; set; }
        public string? ModuleName           { get; set; }
        public string? EntityType           { get; set; }
        public string? EntityId             { get; set; }
        public DateTime? StartDateUtc       { get; set; }
        public DateTime? EndDateUtc         { get; set; }
        public bool    IsPublished          { get; set; } = true;
        public bool    IsPinned             { get; set; }
        public bool    IsPopup              { get; set; }
        public bool    IsBanner             { get; set; }
        public bool    RequireAcknowledgement { get; set; }
        public bool    AllowDismiss         { get; set; } = true;
        public List<AppNoteTargetRequest> Targets { get; set; } = new();
    }

    // ── Lookup DTO ────────────────────────────────────────────────────────────
    public class AppLookupDto
    {
        public string  LookupTypeCode { get; set; } = string.Empty;
        public string  ValueCode      { get; set; } = string.Empty;
        public string  DisplayText    { get; set; } = string.Empty;
        public int     SortOrder      { get; set; }
        public bool    IsDefault      { get; set; }
        public string? MetadataJson   { get; set; }
    }

    // ── Menu Definition DTO ───────────────────────────────────────────────────
    public class AppMenuDefinitionDto
    {
        public string  MenuCode    { get; set; } = string.Empty;
        public string  MenuName    { get; set; } = string.Empty;
        public string? ModuleName  { get; set; }
        public string? RoutePath   { get; set; }
        public string? IconCss     { get; set; }
        public int     SortOrder   { get; set; }
    }

    // ── Generic API Response ──────────────────────────────────────────────────
    public class CommApiResponse<T>
    {
        public bool         Success { get; set; }
        public string       Message { get; set; } = string.Empty;
        public T?           Data    { get; set; }
        public List<string> Errors  { get; set; } = new();

        public static CommApiResponse<T> Ok(T data, string message = "Success") =>
            new() { Success = true, Message = message, Data = data };

        public static CommApiResponse<T> Fail(string message, List<string>? errors = null) =>
            new() { Success = false, Message = message, Errors = errors ?? new() };
    }
}
