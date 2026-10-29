using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Accounts.Configuration;
using Accounts.Data;
using Accounts.DTOs;
using Accounts.Models;
using Accounts.Services.Fax;
using Accounts.Services.Interfaces;
using Accounts.Services.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Controllers;

[ApiController,Route("api/fax"),Authorize,Produces("application/json")]
public sealed class FaxController(ApplicationDbContext db,ITenantService tenant,TenantPermissionService tenantPermissions,RbacService rbac,IFaxDispatchService dispatch,IFaxProvider provider,IOptions<FaxOptions> options) : ControllerBase
{
    private const string RoutePath="/email/fax"; private readonly FaxOptions _options=options.Value;
    [HttpGet("bootstrap")]
    public async Task<IActionResult> Bootstrap(CancellationToken ct)
    {
        if(!await HasActionAsync("VIEW",ct))return Forbid();
        return Ok(new FaxBootstrapDto(new {enabled=_options.Enabled,providerConfigured=provider.IsConfigured,providerName=provider.Name,_options.MaximumFileCount,_options.MaximumFileSizeBytes,_options.MaximumTotalSizeBytes,_options.AllowedExtensions},new {canView=true,canSend=await HasActionAsync("ADD",ct),canManageRecipients=await HasActionAsync("EDIT",ct),canReconcile=await HasActionAsync("PROCESS",ct),canConfigure=tenant.IsTenantAdmin}));
    }
    [HttpGet("transmissions")]
    public async Task<IActionResult> List(CancellationToken ct){if(!await HasActionAsync("VIEW",ct))return Forbid();return Ok(await db.FaxTransmissions.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).Take(2000).Select(x=>FaxDispatchService.ToDto(x)).ToListAsync(ct));}
    [HttpGet("transmissions/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id,CancellationToken ct)
    {
        if(!await HasActionAsync("VIEW",ct))return Forbid();var row=await db.FaxTransmissions.AsNoTracking().Include(x=>x.Attachments).Include(x=>x.Events).SingleOrDefaultAsync(x=>x.Id==id,ct);if(row==null)return NotFound();
        return Ok(new {transmission=FaxDispatchService.ToDto(row),attachments=row.Attachments.OrderBy(x=>x.SortOrder).Select(x=>new FaxAttachmentDto(x.Id,x.DisplayName,x.ContentType,x.SizeBytes,x.PageCount,x.SortOrder)),events=row.Events.OrderBy(x=>x.OccurredAtUtc).Select(x=>new FaxStatusEventDto(x.Id,x.PreviousStatus,x.Status,x.Source,x.Detail,x.OccurredAtUtc))});
    }
    [HttpPost("preview"),RequestSizeLimit(55L*1024*1024)]
    public async Task<IActionResult> Preview(CancellationToken ct)
    {
        if(!await HasActionAsync("ADD",ct))return Forbid();try{var parsed=await ReadSubmissionAsync(ct);var result=await dispatch.PreviewAsync(parsed.Metadata,parsed.Files,ct);Response.Headers.CacheControl="no-store";Response.Headers["X-Fax-Confirmation-Token"]=result.ConfirmationToken;return File(result.Pdf,"application/pdf","fax-preview.pdf");}catch(InvalidOperationException ex){return BadRequest(new{message=ex.Message});}catch(IOException){return BadRequest(new{message="The fax upload was incomplete. Re-select the document and try again."});}
    }
    [HttpPost("transmissions"),RequestSizeLimit(55L*1024*1024)]
    public async Task<IActionResult> Send(CancellationToken ct)
    {
        if(!await HasActionAsync("ADD",ct))return Forbid();if(!Guid.TryParse(Request.Headers["X-Idempotency-Key"],out var key))return BadRequest(new{message="A valid X-Idempotency-Key GUID is required."});var token=Request.Headers["X-Fax-Confirmation-Token"].ToString();if(string.IsNullOrWhiteSpace(token))return BadRequest(new{message="Preview confirmation is required."});
        try{var parsed=await ReadSubmissionAsync(ct);return Ok(await dispatch.SendAsync(parsed.Metadata,parsed.Files,key,token,User.FindFirstValue(ClaimTypes.NameIdentifier)??"unknown",ct));}catch(InvalidOperationException ex){return BadRequest(new{message=ex.Message});}catch(IOException){return BadRequest(new{message="The fax upload was incomplete. Re-select the document and try again."});}
    }
    [HttpPost("transmissions/{id:guid}/reconcile")]
    public async Task<IActionResult> Reconcile(Guid id,CancellationToken ct){if(!await HasActionAsync("PROCESS",ct))return Forbid();var row=await dispatch.ReconcileAsync(id,User.FindFirstValue(ClaimTypes.NameIdentifier),ct);return row==null?NotFound():Ok(row);}

    [HttpGet("recipients")]
    public async Task<IActionResult> Recipients(CancellationToken ct){if(!await HasActionAsync("VIEW",ct))return Forbid();return Ok(await db.FaxRecipients.AsNoTracking().OrderBy(x=>x.OrganizationName).ThenBy(x=>x.Name).Select(x=>RecipientDto(x,false)).ToListAsync(ct));}
    [HttpGet("recipients/{id:guid}")]
    public async Task<IActionResult> Recipient(Guid id,CancellationToken ct){if(!await HasActionAsync("EDIT",ct))return Forbid();var row=await db.FaxRecipients.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);return row==null?NotFound():Ok(RecipientDto(row,true));}
    [HttpPost("recipients")]
    public async Task<IActionResult> CreateRecipient(SaveFaxRecipientDto dto,CancellationToken ct){if(!await HasActionAsync("EDIT",ct))return Forbid();var normalized=NormalizeFax(dto.FaxNumber);if(normalized==null)return BadRequest(new{message="Enter a valid international fax number."});if(await db.FaxRecipients.AnyAsync(x=>x.NormalizedFaxNumber==normalized,ct))return Conflict(new{message="This fax number is already registered."});var user=User.FindFirstValue(ClaimTypes.NameIdentifier)??"unknown";var row=new FaxRecipient{TenantId=tenant.RequiredTenantId,Name=Required(dto.Name,"Recipient name",160),OrganizationName=Required(dto.OrganizationName,"Organization",200),Department=Clean(dto.Department,120),FaxNumber=normalized,NormalizedFaxNumber=normalized,IsVerified=dto.IsVerified,VerifiedAtUtc=dto.IsVerified?DateTime.UtcNow:null,VerifiedByUserId=dto.IsVerified?user:null,IsActive=dto.IsActive,CreatedByUserId=user};db.FaxRecipients.Add(row);await db.SaveChangesAsync(ct);return Ok(RecipientDto(row,true));}
    [HttpPut("recipients/{id:guid}")]
    public async Task<IActionResult> UpdateRecipient(Guid id,SaveFaxRecipientDto dto,CancellationToken ct){if(!await HasActionAsync("EDIT",ct))return Forbid();var row=await db.FaxRecipients.SingleOrDefaultAsync(x=>x.Id==id,ct);if(row==null)return NotFound();if(!string.IsNullOrWhiteSpace(dto.RowVersion))db.Entry(row).Property(x=>x.RowVersion).OriginalValue=Convert.FromBase64String(dto.RowVersion);var normalized=NormalizeFax(dto.FaxNumber);if(normalized==null)return BadRequest(new{message="Enter a valid international fax number."});if(await db.FaxRecipients.AnyAsync(x=>x.Id!=id&&x.NormalizedFaxNumber==normalized,ct))return Conflict(new{message="This fax number is already registered."});row.Name=Required(dto.Name,"Recipient name",160);row.OrganizationName=Required(dto.OrganizationName,"Organization",200);row.Department=Clean(dto.Department,120);row.FaxNumber=normalized;row.NormalizedFaxNumber=normalized;row.IsActive=dto.IsActive;if(dto.IsVerified&&!row.IsVerified){row.VerifiedAtUtc=DateTime.UtcNow;row.VerifiedByUserId=User.FindFirstValue(ClaimTypes.NameIdentifier);}else if(!dto.IsVerified){row.VerifiedAtUtc=null;row.VerifiedByUserId=null;}row.IsVerified=dto.IsVerified;row.UpdatedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync(ct);return Ok(RecipientDto(row,true));}

    private async Task<(FaxSendMetadataDto Metadata,List<FaxUpload> Files)> ReadSubmissionAsync(CancellationToken ct)
    {
        if(!Request.HasFormContentType)
            throw new InvalidOperationException("Multipart form data is required.");

        // Use ASP.NET Core's multipart parser instead of reading Request.Body a
        // second time. A form feature can already have inspected/buffered the
        // request before the action runs; a second MultipartReader then fails
        // with "Unexpected end of Stream". Keep the per-file buffer threshold
        // above the accepted fax size so protected documents remain in memory.
        var formOptions=new FormOptions
        {
            MemoryBufferThreshold=checked((int)Math.Min(_options.MaximumFileSizeBytes,int.MaxValue)),
            MultipartBodyLengthLimit=_options.MaximumTotalSizeBytes+64*1024,
            MultipartHeadersCountLimit=32,
            MultipartHeadersLengthLimit=16*1024,
            ValueLengthLimit=32*1024,
            ValueCountLimit=32,
            KeyLengthLimit=256
        };
        var formFeature=HttpContext.Features.Get<IFormFeature>();
        if(formFeature?.Form==null)
        {
            formFeature=new FormFeature(Request,formOptions);
            HttpContext.Features.Set(formFeature);
        }
        var form=await formFeature.ReadFormAsync(ct);

        byte[] metadataBytes;
        var metadataValue=form["metadata"].FirstOrDefault();
        if(!string.IsNullOrWhiteSpace(metadataValue))
            metadataBytes=System.Text.Encoding.UTF8.GetBytes(metadataValue);
        else
        {
            var metadataFile=form.Files.FirstOrDefault(x=>string.Equals(x.Name,"metadata",StringComparison.OrdinalIgnoreCase))
                ??throw new InvalidOperationException("Fax metadata is required.");
            await using var metadataStream=metadataFile.OpenReadStream();
            metadataBytes=await ReadBoundedAsync(metadataStream,32*1024,ct);
        }
        var metadata=JsonSerializer.Deserialize<FaxSendMetadataDto>(metadataBytes,new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ??throw new InvalidOperationException("Fax metadata is invalid.");

        var submittedFiles=form.Files.Where(x=>string.Equals(x.Name,"files",StringComparison.OrdinalIgnoreCase)).ToList();
        if(submittedFiles.Count>_options.MaximumFileCount)
            throw new InvalidOperationException($"A maximum of {_options.MaximumFileCount} files is allowed.");
        var files=new List<FaxUpload>(submittedFiles.Count);
        long total=0;
        foreach(var upload in submittedFiles)
        {
            var fileName=Path.GetFileName(string.IsNullOrWhiteSpace(upload.FileName)?"document.pdf":upload.FileName);
            var extension=Path.GetExtension(fileName);
            if(!_options.AllowedExtensions.Contains(extension,StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"File type {extension} is not allowed.");
            await using var source=upload.OpenReadStream();
            var content=await ReadBoundedAsync(source,_options.MaximumFileSizeBytes,ct);
            total+=content.LongLength;
            if(total>_options.MaximumTotalSizeBytes)
                throw new InvalidOperationException("Total attachment size exceeds the configured limit.");
            if(content.Length<5||content[0]!='%'||content[1]!='P'||content[2]!='D'||content[3]!='F'||content[4]!='-')
                throw new InvalidOperationException($"{fileName} is not a valid PDF file.");
            files.Add(new FaxUpload(fileName,"application/pdf",content,Convert.ToHexString(SHA256.HashData(content)),0));
        }
        return (metadata,files);
    }
    private static async Task<byte[]> ReadBoundedAsync(Stream source,long limit,CancellationToken ct){using var memory=new MemoryStream();var buffer=new byte[81920];long total=0;int read;while((read=await source.ReadAsync(buffer,ct))>0){total+=read;if(total>limit)throw new InvalidOperationException("Uploaded content exceeds the configured size limit.");await memory.WriteAsync(buffer.AsMemory(0,read),ct);}return memory.ToArray();}
    private async Task<bool> HasActionAsync(string action,CancellationToken ct){if(TenantPermissionService.IsSuperAdmin(User))return false;if(TenantPermissionService.IsTenantAdmin(User))return await tenantPermissions.HasMenuRouteAsync(User,[RoutePath],action,ct);if(!tenant.TenantId.HasValue)return false;var userId=User.FindFirstValue(ClaimTypes.NameIdentifier);if(string.IsNullOrWhiteSpace(userId))return false;var staff=await db.Persons.AsNoTracking().Where(x=>x.IdentityUserId==userId&&x.Staff!=null).Select(x=>(Guid?)x.Staff!.StaffId).FirstOrDefaultAsync(ct);var menu=await db.Menus.AsNoTracking().Where(x=>x.IsActive&&x.Route==RoutePath).Select(x=>(int?)x.Id).FirstOrDefaultAsync(ct);if(!staff.HasValue||!menu.HasValue)return false;var normalized=action.ToUpperInvariant();return normalized=="VIEW"&&await rbac.HasAccessAsync(staff.Value,$"MENU_{menu}")||await rbac.HasAccessAsync(staff.Value,$"MENU_{menu}_{normalized}");}
    private static FaxRecipientDto RecipientDto(FaxRecipient x,bool full)=>new(x.Id,x.Name,x.OrganizationName,x.Department,FaxDispatchService.Mask(x.NormalizedFaxNumber),full?x.NormalizedFaxNumber:null,x.IsVerified,x.VerifiedAtUtc,x.IsActive,Convert.ToBase64String(x.RowVersion));
    private static string? NormalizeFax(string? value){if(string.IsNullOrWhiteSpace(value))return null;var chars=value.Where((c,i)=>char.IsDigit(c)||(c=='+'&&i==0)).ToArray();var result=new string(chars);if(!result.StartsWith('+')||result.Length<9||result.Length>16)return null;return result;}
    private static string Required(string? value,string name,int max){var clean=Clean(value,max);return clean??throw new InvalidOperationException($"{name} is required.");}private static string? Clean(string? value,int max)=>string.IsNullOrWhiteSpace(value)?null:value.Trim()[..Math.Min(value.Trim().Length,max)];
}
