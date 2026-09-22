using System.Text.Json.Serialization;

namespace Accounts.Models.SpListRows;

public sealed class AnnualReportFilterListRow
{
    public int Id { get; set; }
    public int ReportTypeId { get; set; }
    public string? ReportType { get; set; }
    public int CategoryId { get; set; }
    [JsonPropertyName("cat_Name")]
    public string? Cat_Name { get; set; }
    [JsonPropertyName("isInclude")]
    public bool IsInclude { get; set; }
}

public sealed class AnnualCategoryMonthAmountRow
{
    public int CategoryId { get; set; }
    public string? Cat_Name { get; set; }
    public int? CalendarYear { get; set; }
    public int? CalendarMonth { get; set; }
    public decimal Amount { get; set; }
}

public sealed class AnnualMonthWiseListRow
{
    public long HeaderId { get; set; }
    public string? FiscalYear { get; set; }
    public string? Remarks { get; set; }
    public bool IsApproved { get; set; }
    public decimal Jul { get; set; }
    public decimal Aug { get; set; }
    public decimal Sep { get; set; }
    public decimal Oct { get; set; }
    public decimal Nov { get; set; }
    public decimal Dec { get; set; }
    public decimal Jan { get; set; }
    public decimal Feb { get; set; }
    public decimal Mar { get; set; }
    public decimal Apr { get; set; }
    public decimal May { get; set; }
    public decimal Jun { get; set; }
    public decimal Total { get; set; }
}

public sealed class AnnualCategoryWiseAmountRow
{
    public long HeaderId { get; set; }
    public string? FiscalYear { get; set; }
    public int CategoryId { get; set; }
    public string? Cat_Name { get; set; }
    public decimal Amount { get; set; }
}
