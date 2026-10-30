using Accounts.DTOs;
using Accounts.Services.Ocr.HealthNet;
using Accounts.Services.Ocr.HealthNet.Models;
using Accounts.Services.Ocr.Interfaces;

namespace Accounts.Services.Ocr.Parsers;

/// <summary>
/// Adapts the coordinate-aware Health Net EOP engine to the common six-payer
/// OCR contract. Persistence and claim posting remain in the current tenant-
/// aware OCR pipeline; the legacy engine is used only for document parsing.
/// </summary>
public sealed class HealthNetDocumentParser : Accounts.Services.Ocr.Interfaces.IHealthNetDocumentParser
{
    public ParsedEobHeader Parse(byte[] pdfBytes)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        if (pdfBytes.Length == 0)
            throw new InvalidDataException("The uploaded Health Net PDF is empty.");

        var normalizer = new HealthNetTextNormalizer();
        var classifier = new HealthNetPageClassifier(normalizer);
        var reader = new HealthNetPdfReader(classifier, normalizer);
        var serviceLineParser = new HealthNetServiceLineParser();
        var claimParser = new HealthNetClaimParser(serviceLineParser, normalizer);
        var engine = new Accounts.Services.Ocr.HealthNet.HealthNetDocumentParser(
            reader,
            new HealthNetHeaderParser(),
            claimParser,
            serviceLineParser,
            new HealthNetSummaryParser(),
            new HealthNetExplanationCodeParser());

        using var stream = new MemoryStream(pdfBytes, writable: false);
        var result = engine.ParseAsync(stream).GetAwaiter().GetResult();

        if (!result.Success)
        {
            var details = result.Errors.FirstOrDefault()
                ?? result.Message
                ?? "The Health Net PDF could not be parsed.";
            throw new InvalidDataException(details);
        }

        return Map(result);
    }

    private static ParsedEobHeader Map(HealthNetOCRResultViewModel source)
    {
        var header = source.Header;
        var parsed = new ParsedEobHeader
        {
            DocTitle = header.Title ?? "HEALTH NET EXPLANATION OF PAYMENT",
            InsuranceName = header.InsuranceName ?? "Health Net",
            InsuranceAddress = header.InsuranceAddress,
            ProviderName = header.ServiceProviderName,
            ProviderAddress = header.ProviderAddress,
            ProviderId = header.ServiceProviderNo ?? header.PayeeNo,
            NPI = header.ProviderNpi,
            CheckEFT = header.RemittanceNo,
            StatementDateParsed = header.ProcessDate,
            StatementDate = header.ProcessDate?.ToString("MM/dd/yyyy"),
            Source = "HEALTH NET Docs",
            Warnings = source.Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        parsed.Patients = source.Claims.Select(MapClaim).ToList();
        parsed.DocumentTotals = MapTotals(source);
        return parsed;
    }

    private static ParsedPatient MapClaim(HealthNetClaimModel claim)
    {
        var totals = claim.Totals;
        return new ParsedPatient
        {
            PatientName = claim.PatientNameRaw,
            MID = claim.SubIdCinRaw,
            PatientNo = claim.AccountNoRaw,
            ICN = claim.ClaimNo,
            Billed = totals.BilledAmount,
            Allowed = totals.AllowedAmount,
            CoInsurance = totals.PatientCostShareAmount,
            NonCovered = totals.AmountNotAllowed,
            OtherAdjustments = totals.CobAllowedAmount,
            OtherPlanPayment = totals.CobPaidAmount,
            ProviderPaid = totals.BenefitPayable,
            PatientResponsibility = totals.PatientResponsibility,
            Procedures = claim.ServiceLines.Select(MapLine).ToList()
        };
    }

    private static ParsedProcedure MapLine(HealthNetClaimLineModel line)
    {
        var procedure = new ParsedProcedure
        {
            DateOfServiceParsed = line.ServiceDateFrom,
            DateOfService = line.ServiceDateFrom?.ToString("MM/dd/yyyy"),
            ProcedureCode = line.CPTRev,
            Modifiers = line.Modifiers.Count == 0 ? null : string.Join(", ", line.Modifiers),
            Billed = line.BilledAmount,
            Allowed = line.AllowedAmount,
            CoInsurance = line.PatientCostShareAmount,
            NonCovered = line.AmountNotAllowed,
            OtherAdjustments = line.CobAllowedAmount,
            OtherPlan = line.CobPaidAmount,
            ProviderPaid = line.BenefitPayable,
            PatientResponsibility = line.PatientResponsibility,
            GrpRows = line.ExCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => new ParsedGrp { GRPCode = code.Trim() })
                .ToList()
        };

        if (!string.IsNullOrWhiteSpace(line.QPA))
            procedure.Remarks.Add(line.QPA.Trim());

        return procedure;
    }

    private static ParsedDocumentTotals MapTotals(HealthNetOCRResultViewModel source)
    {
        decimal Sum(Func<HealthNetClaimTotalsModel, decimal?> selector) =>
            source.Claims.Sum(claim => selector(claim.Totals) ?? 0m);

        return new ParsedDocumentTotals
        {
            ClaimCount = source.Summary.TotalClaims ?? source.Claims.Count,
            Billed = Sum(totals => totals.BilledAmount),
            Allowed = Sum(totals => totals.AllowedAmount),
            CoInsurance = Sum(totals => totals.PatientCostShareAmount),
            RC = Sum(totals => totals.AmountNotAllowed),
            Adjustment = Sum(totals => totals.CobAllowedAmount),
            OtherPlanPayment = Sum(totals => totals.CobPaidAmount),
            ProviderPaid = source.Summary.TotalClaimsPayable ?? Sum(totals => totals.BenefitPayable),
            CheckAmount = source.Summary.TotalCheckAmount
                ?? source.Summary.TotalClaimsPayable
                ?? Sum(totals => totals.BenefitPayable)
        };
    }
}
