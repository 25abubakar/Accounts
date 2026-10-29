using Accounts.Configuration;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Services.Fax;

public sealed class FaxDispatchService(ApplicationDbContext db,ITenantService tenant,IFaxDocumentService documents,IFaxProvider provider,IFaxStatusService statuses,IOptions<FaxOptions> options) : IFaxDispatchService
{
    private readonly FaxOptions _options=options.Value;
    public async Task<(byte[] Pdf,string ConfirmationToken)> PreviewAsync(FaxSendMetadataDto metadata,IReadOnlyList<FaxUpload> uploads,CancellationToken ct)
    {
        var recipient=await RequireRecipient(metadata.RecipientId,ct); ValidateMetadata(metadata);
        var document=documents.Prepare(metadata,recipient,uploads);
        return (document.Content,documents.CreateConfirmationToken(document));
    }

    public async Task<FaxTransmissionDto> SendAsync(FaxSendMetadataDto metadata,IReadOnlyList<FaxUpload> uploads,Guid idempotencyKey,string confirmationToken,string userId,CancellationToken ct)
    {
        var duplicate=await db.FaxTransmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.IdempotencyKey==idempotencyKey,ct);
        if(duplicate!=null) return ToDto(duplicate);
        var recipient=await RequireRecipient(metadata.RecipientId,ct); ValidateMetadata(metadata);
        var expectedDocument=documents.Prepare(metadata,recipient,uploads);
        var document=documents.ResolveConfirmationToken(confirmationToken,expectedDocument)
            ?? throw new InvalidOperationException("The fax preview expired or no longer matches the selected files. Generate a new preview.");
        var row=new FaxTransmission
        {
            TenantId=tenant.RequiredTenantId,IdempotencyKey=idempotencyKey,RecipientId=recipient.Id,
            RecipientName=recipient.Name,RecipientOrganization=recipient.OrganizationName,RecipientFaxNumber=recipient.NormalizedFaxNumber,
            PatientExternalId=Clean(metadata.PatientExternalId,120),PatientDisplayName=Clean(metadata.PatientDisplayName,200),AppointmentExternalId=Clean(metadata.AppointmentExternalId,120),
            Subject=metadata.Subject.Trim(),Status=FaxStatuses.Preparing,Provider=provider.Name,AttachmentCount=document.Attachments.Count,PageCount=document.PageCount,CreatedByUserId=userId
        };
        row.Reference=$"FAX-{DateTime.UtcNow:yyyyMMdd}-{row.Id.ToString("N")[..8].ToUpperInvariant()}";
        foreach(var file in document.Attachments.Select((file,index)=>(file,index))) row.Attachments.Add(new FaxAttachmentMetadata{TenantId=row.TenantId,DisplayName=file.file.DisplayName,ContentType=file.file.ContentType,SizeBytes=file.file.Content.LongLength,Sha256=file.file.Sha256,PageCount=file.file.PageCount,SortOrder=file.index});
        row.Events.Add(new FaxStatusEvent{TenantId=row.TenantId,Status=FaxStatuses.Preparing,Source="Application",ActorUserId=userId,Detail="Transmission prepared after recipient confirmation."});
        db.FaxTransmissions.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The tenant/idempotency unique index is the concurrency authority. If two
            // identical requests race, only the persisted request may reach the provider.
            db.ChangeTracker.Clear();
            var replay = await db.FaxTransmissions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
            if (replay != null) return ToDto(replay);
            throw;
        }
        await statuses.TransitionAsync(row,FaxStatuses.Submitting,"Application",null,null,userId,ct);
        try
        {
            var result=await provider.SubmitAsync(recipient.NormalizedFaxNumber,row.Reference,document.Content,ct);
            row.Provider=result.Provider; row.ProviderFaxId=result.ProviderFaxId;
            await statuses.TransitionAsync(row,result.Status,"Provider","Provider accepted the transmission.",null,userId,ct);
        }
        catch(HttpRequestException ex) when(ex.StatusCode==null)
        {
            row.FailureCode="PROVIDER_RESPONSE_UNKNOWN"; row.FailureSummary="Provider acceptance could not be confirmed; reconciliation is required.";
            await statuses.TransitionAsync(row,FaxStatuses.AcceptanceUnknown,"Application",row.FailureSummary,null,userId,ct);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        {
            // The provider may have received the body before the client disconnected.
            // Persist uncertainty and never auto-resend the medical document.
            row.FailureCode="PROVIDER_RESPONSE_UNKNOWN";
            row.FailureSummary="The request was cancelled before provider acceptance could be confirmed; reconciliation is required.";
            await statuses.TransitionAsync(row,FaxStatuses.AcceptanceUnknown,"Application",row.FailureSummary,null,userId,CancellationToken.None);
            throw;
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            row.FailureCode="PROVIDER_REJECTED"; row.FailureSummary="The provider rejected this transmission.";
            await statuses.TransitionAsync(row,FaxStatuses.Failed,"Application",row.FailureSummary,null,userId,ct);
        }
        return ToDto(row);
    }

    public async Task<FaxTransmissionDto?> ReconcileAsync(Guid id,string? actorUserId,CancellationToken ct)
    {
        var row=await db.FaxTransmissions.SingleOrDefaultAsync(x=>x.Id==id,ct); if(row==null)return null;
        if(FaxStatuses.Terminal.Contains(row.Status))return ToDto(row);
        if(string.IsNullOrWhiteSpace(row.ProviderFaxId))
        {
            if(row.Status==FaxStatuses.AcceptanceUnknown) await statuses.TransitionAsync(row,FaxStatuses.ReconciliationRequired,"Reconciliation","Provider transmission ID is unavailable; manual provider review is required.",null,actorUserId,ct);
            return ToDto(row);
        }
        var state=await provider.GetStatusAsync(row.ProviderFaxId,ct); row.FailureCode=state.FailureCode; row.FailureSummary=state.FailureSummary;
        if(!string.Equals(row.Status,state.Status,StringComparison.OrdinalIgnoreCase)) await statuses.TransitionAsync(row,state.Status,"Reconciliation",state.FailureSummary,null,actorUserId,ct); else {row.LastCheckedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync(ct);}
        return ToDto(row);
    }

    public async Task<int> ReconcilePendingAsync(int tenantId,CancellationToken ct)
    {
        var cutoff=DateTime.UtcNow.AddMinutes(-Math.Max(1,_options.ReconciliationMinimumAgeMinutes));
        var ids=await db.FaxTransmissions.Where(x=>x.TenantId==tenantId && x.CreatedAtUtc<=cutoff && x.Status != FaxStatuses.Delivered && x.Status != FaxStatuses.Failed && x.ProviderFaxId!=null).OrderBy(x=>x.LastCheckedAtUtc??x.CreatedAtUtc).Select(x=>x.Id).Take(Math.Clamp(_options.ReconciliationBatchSize,1,200)).ToListAsync(ct);
        var count=0; foreach(var id in ids){await ReconcileAsync(id,null,ct);count++;} return count;
    }

    private async Task<FaxRecipient> RequireRecipient(Guid id,CancellationToken ct)=>await db.FaxRecipients.SingleOrDefaultAsync(x=>x.Id==id&&x.IsActive&&x.IsVerified,ct)??throw new InvalidOperationException("Select an active verified fax recipient.");
    private static void ValidateMetadata(FaxSendMetadataDto dto){if(string.IsNullOrWhiteSpace(dto.Subject)||dto.Subject.Trim().Length>160)throw new InvalidOperationException("A subject of up to 160 characters is required.");if(dto.CoverMessage?.Length>2000)throw new InvalidOperationException("Cover message cannot exceed 2,000 characters.");}
    private static string? Clean(string? value,int max)=>string.IsNullOrWhiteSpace(value)?null:value.Trim()[..Math.Min(value.Trim().Length,max)];
    public static FaxTransmissionDto ToDto(FaxTransmission x)=>new(x.Id,x.Reference,x.PatientExternalId,x.PatientDisplayName,x.AppointmentExternalId,x.RecipientName,x.RecipientOrganization,Mask(x.RecipientFaxNumber),x.Subject,x.Status,x.Provider,x.ProviderFaxId,x.AttachmentCount,x.PageCount,x.CreatedAtUtc,x.AcceptedAtUtc,x.DeliveredAtUtc,x.LastCheckedAtUtc,x.FailureSummary);
    public static string Mask(string value)=>value.Length<=4?"****":new string('*',Math.Min(8,value.Length-4))+value[^4..];
}
