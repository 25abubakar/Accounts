using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounts.Configuration;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Controllers;

[ApiController,Route("api/fax/webhooks/phaxio"),AllowAnonymous,IgnoreAntiforgeryToken]
public sealed class FaxWebhookController(ApplicationDbContext db,IFaxStatusService statuses,IOptions<FaxOptions> options) : ControllerBase
{
    [HttpPost,RequestSizeLimit(256*1024)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var config=options.Value;
        if(string.IsNullOrWhiteSpace(config.WebhookSecret))return StatusCode(StatusCodes.Status503ServiceUnavailable);
        if(!Request.HasFormContentType)return BadRequest();
        var form=await Request.ReadFormAsync(ct);var supplied=Request.Headers["X-Phaxio-Signature"].ToString();
        var callbackUrl=config.CallbackUrl??$"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}{Request.QueryString}";
        var canonical=new StringBuilder(callbackUrl);
        foreach(var pair in form.OrderBy(x=>x.Key,StringComparer.Ordinal))foreach(var value in pair.Value)canonical.Append(pair.Key).Append(value);
        using var hmac=new HMACSHA1(Encoding.UTF8.GetBytes(config.WebhookSecret));
        var expected=Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
        if(string.IsNullOrWhiteSpace(supplied)||!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected),Encoding.ASCII.GetBytes(supplied.ToLowerInvariant())))return Unauthorized();
        if(!form.TryGetValue("fax",out var faxJson))return BadRequest();
        using var document=JsonDocument.Parse(faxJson.ToString());var fax=document.RootElement;
        var providerId=fax.TryGetProperty("id",out var idNode)?idNode.ToString():null;if(string.IsNullOrWhiteSpace(providerId))return BadRequest();
        var row=await db.FaxTransmissions.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.Provider=="Phaxio"&&x.ProviderFaxId==providerId,ct);if(row==null)return Ok();
        var success=form.TryGetValue("success",out var successValue)&&bool.TryParse(successValue,out var parsed)&&parsed;
        var providerStatus=fax.TryGetProperty("status",out var statusNode)?statusNode.GetString()?.ToLowerInvariant():null;
        var next=success||providerStatus is "success" or "delivered"?FaxStatuses.Delivered:providerStatus is "failure" or "failed"?FaxStatuses.Failed:FaxStatuses.Transmitting;
        var detail=fax.TryGetProperty("error_message",out var errorNode)?errorNode.GetString():null;
        var eventReference=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{providerId}|{next}|{supplied}")));
        if(await db.FaxStatusEvents.IgnoreQueryFilters().AnyAsync(x=>x.TenantId==row.TenantId&&x.ProviderEventReference==eventReference,ct))return Ok();
        // A delayed/out-of-order provider callback must never regress a terminal result.
        if(FaxStatuses.Terminal.Contains(row.Status))return Ok();
        if(next==FaxStatuses.Failed){row.FailureCode="PROVIDER_FAILED";row.FailureSummary=string.IsNullOrWhiteSpace(detail)?"Provider reported delivery failure.":detail[..Math.Min(detail.Length,500)];}
        await statuses.TransitionAsync(row,next,"Webhook",row.FailureSummary,eventReference,null,ct);
        return Ok();
    }
}
