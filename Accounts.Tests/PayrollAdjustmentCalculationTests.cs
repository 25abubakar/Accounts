using Accounts.Models;
using Accounts.Services.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class PayrollAdjustmentCalculationTests
{
    [Fact]
    public void Recalculate_PositiveAdjustment_ReducesAttendanceDeductionWithoutIncreasingGross()
    {
        var line = BaseLine();
        line.AttendanceDeduction = 10_000m;
        line.AttendanceAdjustment = 4_000m;
        line.IsAttendanceAdjustmentApproved = true;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(100_000m, line.TaxableIncome);
        Assert.Equal(100_000m, line.GrossPay);
        Assert.Equal(6_000m, line.TotalDeduction);
        Assert.Equal(94_000m, line.NetPay);
    }

    [Fact]
    public void Recalculate_FullPositiveAdjustment_KeepsOriginalDeductionVisibleAndFullyRelievesIt()
    {
        var line = BaseLine();
        line.AttendanceDeduction = 5_000m;
        line.AttendanceAdjustment = 5_000m;
        line.AttendanceAdjustmentRemarks = "Approved deduction relief";
        line.IsAttendanceAdjustmentApproved = true;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(5_000m, line.AttendanceDeduction);
        Assert.Equal(5_000m, line.AttendanceAdjustment);
        Assert.Equal(0m, line.TotalDeduction);
        Assert.Equal(100_000m, line.NetPay);
    }

    [Fact]
    public void Recalculate_PositiveAdjustment_IsCappedAtAttendanceDeduction()
    {
        var line = BaseLine();
        line.AttendanceDeduction = 5_000m;
        line.AttendanceAdjustment = 8_000m;
        line.IsAttendanceAdjustmentApproved = true;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(0m, line.TotalDeduction);
        Assert.Equal(100_000m, line.NetPay);
    }

    [Fact]
    public void Recalculate_NegativeAdjustment_IsAnAdditionalDeduction()
    {
        var line = BaseLine();
        line.AttendanceDeduction = 5_000m;
        line.AttendanceAdjustment = -2_000m;
        line.IsAttendanceAdjustmentApproved = true;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(7_000m, line.TotalDeduction);
        Assert.Equal(93_000m, line.NetPay);
    }

    [Fact]
    public void Recalculate_PendingAdjustment_DoesNotChangeNetPay()
    {
        var line = BaseLine();
        line.AttendanceDeduction = 5_000m;
        line.AttendanceAdjustment = 2_000m;
        line.IsAttendanceAdjustmentApproved = false;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(5_000m, line.TotalDeduction);
        Assert.Equal(95_000m, line.NetPay);
    }

    [Fact]
    public void Recalculate_LegacyActivationSnapshot_DoesNotSuppressOrdinaryAttendanceDeduction()
    {
        var line = BaseLine();
        line.AttendanceDeduction = 7_500m;
        line.IsAttendanceDeductionActive = false;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(7_500m, line.AttendanceDeduction);
        Assert.Equal(7_500m, line.TotalDeduction);
        Assert.Equal(92_500m, line.NetPay);
    }

    [Fact]
    public void Recalculate_ActiveAttendanceDeduction_ReducesPay()
    {
        var line = BaseLine();
        line.AttendanceDeduction = 7_500m;
        line.IsAttendanceDeductionActive = true;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(7_500m, line.TotalDeduction);
        Assert.Equal(92_500m, line.NetPay);
    }

    [Fact]
    public void Recalculate_AssessmentAmount_IsIncludedInTaxableGrossAndNetPay()
    {
        var line = BaseLine();
        line.AllowanceAmount = 10_000m;
        line.AssessmentAmount = 8_000m;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(118_000m, line.TaxableIncome);
        Assert.Equal(118_000m, line.GrossPay);
        Assert.Equal(118_000m, line.NetPay);
    }

    [Fact]
    public void Recalculate_LegacyAllowanceBreakdown_IsPersistedInAllowanceAndGrossTotalsOnce()
    {
        var line = BaseLine();
        line.GeneralAllowanceAmount = 1_000m;
        line.ApptAllowanceAmount = 2_000m;
        line.ShiftAllowanceAmount = 3_000m;
        line.MedicalAllowanceAmount = 4_000m;
        line.NightAllowanceAmount = 5_000m;
        line.TelephoneAllowanceAmount = 6_000m;
        line.TransportAllowanceAmount = 7_000m;

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(28_000m, line.AllowanceAmount);
        Assert.Equal(128_000m, line.GrossPay);
        Assert.Equal(128_000m, line.NetPay);
    }

    private static PayrollLine BaseLine() => new()
    {
        BasicSalary = 100_000m
    };
}
