using System.Net.Http.Json;
using System.Text.Json;
using Accounts.Configuration;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace Accounts.Services.Fax;

public sealed class FaxProvider(IHttpClientFactory clients,IOptions<FaxOptions> options) : IFaxProvider
{
    private readonly FaxOptions _options=options.Value;
    public string Name => string.Equals(_options.Provider,"Sandbox",StringComparison.OrdinalIgnoreCase)?"Sandbox":"Phaxio";
    public bool IsConfigured => _options.Enabled && (Name=="Sandbox" || (!string.IsNullOrWhiteSpace(_options.ApiKey)&&!string.IsNullOrWhiteSpace(_options.ApiSecret)));

    public async Task<FaxProviderSubmission> SubmitAsync(string destination,string reference,byte[] pdf,CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("Fax provider credentials are not configured.");
        if (Name=="Sandbox") return new("Sandbox",$"sandbox-{Guid.NewGuid():N}",FaxStatuses.Accepted);
        using var form=new MultipartFormDataContent();
        form.Add(new StringContent(_options.ApiKey!),"api_key"); form.Add(new StringContent(_options.ApiSecret!),"api_secret");
        form.Add(new StringContent(destination),"to");
        form.Add(new StringContent(reference),"tag[reference]");
        if (!string.IsNullOrWhiteSpace(_options.CallbackUrl))
            form.Add(new StringContent(_options.CallbackUrl),"callback_url");
        var content=new ByteArrayContent(pdf); content.Headers.ContentType=new("application/pdf"); form.Add(content,"file","fax.pdf");
        using var response=await clients.CreateClient("FaxProvider").PostAsync("faxes",form,ct);
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:ct);
        if(!response.IsSuccessStatusCode) throw new HttpRequestException("The fax provider rejected the transmission.",null,response.StatusCode);
        var id=json.TryGetProperty("data",out var data)&&data.TryGetProperty("id",out var idNode)?idNode.ToString():throw new InvalidOperationException("Fax provider response did not include a transmission ID.");
        return new("Phaxio",id,FaxStatuses.Accepted);
    }

    public async Task<FaxProviderState> GetStatusAsync(string providerFaxId,CancellationToken ct)
    {
        if (Name=="Sandbox") return new(FaxStatuses.Delivered);
        var client=clients.CreateClient("FaxProvider");
        var url=$"faxes/{Uri.EscapeDataString(providerFaxId)}?api_key={Uri.EscapeDataString(_options.ApiKey!)}&api_secret={Uri.EscapeDataString(_options.ApiSecret!)}";
        using var response=await client.GetAsync(url,ct); response.EnsureSuccessStatusCode();
        var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:ct);
        var status=json.GetProperty("data").GetProperty("status").GetString()?.ToLowerInvariant();
        return status switch { "success" or "delivered"=>new(FaxStatuses.Delivered), "failure" or "failed"=>new(FaxStatuses.Failed,"PROVIDER_FAILED","Provider reported delivery failure."), "inprogress" or "transmitting"=>new(FaxStatuses.Transmitting), _=>new(FaxStatuses.Accepted) };
    }
}
