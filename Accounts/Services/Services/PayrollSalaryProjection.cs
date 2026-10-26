namespace Accounts.Services.Services;

/// <summary>Read model of the tenant-scoped shared SQL salary projection.</summary>
public sealed class PayrollSalaryProjection
{
    public Guid PersonId { get; set; }
    public int? SalaryScaleId { get; set; }
    public decimal ScaleBasic { get; set; }
    public decimal YearlyIncrement { get; set; }
    public decimal MaxSalary { get; set; }
    public decimal CurrentPay { get; set; }
    public DateOnly? EffectiveScaleDate { get; set; }
}
