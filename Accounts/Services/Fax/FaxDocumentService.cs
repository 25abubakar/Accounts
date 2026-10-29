using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounts.DTOs;
using Accounts.Configuration;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Accounts.Services.Fax;

public sealed class FaxDocumentService(IDataProtectionProvider protectionProvider,IFaxPreviewStore previews) : IFaxDocumentService
{
    static FaxDocumentService()
    {
        // The Accounts API/worker is deployed on Windows. PDFsharp Core does not
        // use installed system fonts unless this opt-in is set before XFont use.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    private readonly IDataProtector _protector = protectionProvider.CreateProtector("Accounts.Fax.Confirmation.v1");

    public FaxPreparedDocument Prepare(FaxSendMetadataDto metadata,FaxRecipient recipient,IReadOnlyList<FaxUpload> uploads)
    {
        if (uploads.Count == 0) throw new InvalidOperationException("Attach at least one PDF document.");
        using var output = new PdfDocument();
        output.Info.Title = "Secure outbound fax";
        output.Info.Subject = metadata.Subject.Trim();
        AddCover(output, metadata, recipient);
        var normalizedUploads = new List<FaxUpload>(uploads.Count);
        foreach (var upload in uploads)
        {
            using var sourceStream = new MemoryStream(upload.Content, writable:false);
            using var source = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages) output.AddPage(page);
            normalizedUploads.Add(upload with { PageCount = source.PageCount });
        }
        var pageCount = output.PageCount;
        using var result = new MemoryStream();
        output.Save(result, closeStream:false);
        var fingerprint = Fingerprint(metadata, recipient.Id, uploads);
        return new FaxPreparedDocument(result.ToArray(), fingerprint, pageCount, normalizedUploads);
    }

    public string CreateConfirmationToken(FaxPreparedDocument document)
    {
        var expiresAtUtc=DateTime.UtcNow.AddMinutes(20);
        var key=previews.Store(document,expiresAtUtc);
        return _protector.Protect(JsonSerializer.Serialize(new Confirmation(key,document.Fingerprint,expiresAtUtc)));
    }

    public FaxPreparedDocument? ResolveConfirmationToken(string token,FaxPreparedDocument expectedDocument)
    {
        try
        {
            var value=JsonSerializer.Deserialize<Confirmation>(_protector.Unprotect(token));
            if(value == null || value.ExpiresAtUtc < DateTime.UtcNow || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(value.Fingerprint), Encoding.UTF8.GetBytes(expectedDocument.Fingerprint))) return null;
            return previews.Get(value.Key,expectedDocument.Fingerprint,DateTime.UtcNow);
        }
        catch (CryptographicException) { return null; }
    }

    private static void AddCover(PdfDocument document,FaxSendMetadataDto metadata,FaxRecipient recipient)
    {
        var page=document.AddPage(); page.Size=PdfSharp.PageSize.A4;
        using var graphics=XGraphics.FromPdfPage(page);
        var title=new XFont("Arial",22,XFontStyleEx.Bold); var label=new XFont("Arial",10,XFontStyleEx.Bold); var text=new XFont("Arial",11);
        graphics.DrawRectangle(new XSolidBrush(XColor.FromArgb(3,105,161)),0,0,page.Width.Point,76);
        graphics.DrawString("SECURE MEDICAL FAX",title,XBrushes.White,new XRect(40,24,page.Width.Point-80,30),XStringFormats.TopLeft);
        var y=105d;
        void Line(string name,string? value){graphics.DrawString(name,label,XBrushes.DarkSlateGray,new XPoint(45,y));graphics.DrawString(string.IsNullOrWhiteSpace(value)?"—":value,text,XBrushes.Black,new XRect(165,y-12,page.Width.Point-210,28),XStringFormats.TopLeft);y+=30;}
        Line("TO",recipient.Name); Line("ORGANIZATION",recipient.OrganizationName); Line("FAX",Mask(recipient.FaxNumber)); Line("SUBJECT",metadata.Subject);
        Line("PATIENT REFERENCE",metadata.PatientExternalId); Line("APPOINTMENT",metadata.AppointmentExternalId);
        y+=10; graphics.DrawString("COVER MESSAGE",label,XBrushes.DarkSlateGray,new XPoint(45,y)); y+=18;
        var message=string.IsNullOrWhiteSpace(metadata.CoverMessage)?"No cover message supplied.":metadata.CoverMessage.Trim();
        graphics.DrawString(message,text,XBrushes.Black,new XRect(45,y,page.Width.Point-90,250),XStringFormats.TopLeft);
        graphics.DrawString("CONFIDENTIALITY NOTICE",label,XBrushes.DarkSlateGray,new XPoint(45,page.Height.Point-105));
        graphics.DrawString("This transmission may contain confidential information. If received in error, do not copy or distribute it and notify the sender.",new XFont("Arial",8),XBrushes.Gray,new XRect(45,page.Height.Point-90,page.Width.Point-90,42),XStringFormats.TopLeft);
    }

    private static string Fingerprint(FaxSendMetadataDto metadata,Guid recipientId,IReadOnlyList<FaxUpload> uploads)
    {
        var canonical=JsonSerializer.Serialize(new { RecipientId=recipientId,metadata.PatientExternalId,metadata.PatientDisplayName,metadata.AppointmentExternalId,Subject=metadata.Subject.Trim(),CoverMessage=metadata.CoverMessage?.Trim(),Files=uploads.Select((x,i)=>new{i,x.DisplayName,Size=x.Content.LongLength,x.Sha256}) });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    private static string Mask(string value)=>value.Length<=4?"****":new string('*',Math.Min(8,value.Length-4))+value[^4..];
    private sealed record Confirmation(string Key,string Fingerprint,DateTime ExpiresAtUtc);
}

public sealed class FaxPreviewStore(IOptions<FaxOptions> options) : IFaxPreviewStore
{
    private sealed record Entry(FaxPreparedDocument Document,DateTime ExpiresAtUtc);
    private readonly object _gate=new();
    private readonly Dictionary<string,Entry> _entries=[];
    private readonly long _maximumBytes=Math.Max(50L*1024*1024,options.Value.MaximumPreviewCacheBytes);
    private long _storedBytes;

    public string Store(FaxPreparedDocument document,DateTime expiresAtUtc)
    {
        lock(_gate)
        {
            RemoveExpired(DateTime.UtcNow);
            while(_storedBytes+document.Content.LongLength>_maximumBytes && _entries.Count>0)
                Remove(_entries.MinBy(pair=>pair.Value.ExpiresAtUtc).Key);
            if(document.Content.LongLength>_maximumBytes)
                throw new InvalidOperationException("The generated fax exceeds the secure preview cache limit.");
            var key=Guid.NewGuid().ToString("N");
            _entries[key]=new(document,expiresAtUtc);
            _storedBytes+=document.Content.LongLength;
            return key;
        }
    }

    public FaxPreparedDocument? Get(string key,string expectedFingerprint,DateTime nowUtc)
    {
        lock(_gate)
        {
            RemoveExpired(nowUtc);
            if(!_entries.TryGetValue(key,out var entry))return null;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(entry.Document.Fingerprint),
                Encoding.UTF8.GetBytes(expectedFingerprint))?entry.Document:null;
        }
    }

    private void RemoveExpired(DateTime nowUtc)
    {
        foreach(var key in _entries.Where(pair=>pair.Value.ExpiresAtUtc<nowUtc).Select(pair=>pair.Key).ToArray())Remove(key);
    }
    private void Remove(string key){if(_entries.Remove(key,out var entry))_storedBytes-=entry.Document.Content.LongLength;}
}
