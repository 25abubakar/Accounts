namespace Accounts.DTOs;

public sealed class ProcessLookupDto
{
    public string LookupType { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
    public int DisplayOrder { get; set; }
    public bool RequiresComments { get; set; }
}

public sealed class ProcessReportSubmissionCapabilityDto
{
    public bool CanSubmit { get; set; }
    public string? Reason { get; set; }
    public Guid? ReportingManagerStaffId { get; set; }
    public string? ReportingManagerName { get; set; }
}

public sealed class ProcessReportListDto
{
    public long Id { get; set; }
    public string? RequestNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SourceModule { get; set; }
    public string? SourceRecordId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string PriorityCode { get; set; } = string.Empty;
    public string PriorityName { get; set; } = string.Empty;
    public string PriorityColor { get; set; } = string.Empty;
    public string StatusCode { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public bool IsTerminal { get; set; }
    public Guid RequesterStaffId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public string? RequesterNumber { get; set; }
    public Guid SubjectStaffId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string? SubjectNumber { get; set; }
    public string? CurrentApproverName { get; set; }
    public bool IsFinalApprover { get; set; }
    public DateTime CreatedDateUtc { get; set; }
    public DateTime? ModifiedDateUtc { get; set; }
    public DateTime? CompletedDateUtc { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ProcessRouteStepDto
{
    public long Id { get; set; }
    public int StepOrder { get; set; }
    public string ApproverName { get; set; } = string.Empty;
    public string? ApproverNumber { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public DateTime? AssignedDateUtc { get; set; }
    public DateTime? ActedDateUtc { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class ProcessActionHistoryDto
{
    public long Id { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public DateTime ActionDateUtc { get; set; }
    public string ToStatusCode { get; set; } = string.Empty;
    public string ToStatusName { get; set; } = string.Empty;
    public string ToStatusColor { get; set; } = string.Empty;
}

public sealed class SubmitProcessReportDto
{
    public Guid? SubjectStaffId { get; set; }
    public string CategoryCode { get; set; } = string.Empty;
    public string PriorityCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SourceModule { get; set; }
    public string? SourceRecordId { get; set; }
}

public sealed class ProcessReportActionDto
{
    public string ActionCode { get; set; } = string.Empty;
    public string? Comments { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
