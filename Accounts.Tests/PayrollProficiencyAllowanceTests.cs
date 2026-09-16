using Accounts.Models;
using Accounts.Services.Services;
using Xunit;

namespace Accounts.Tests;

public sealed class PayrollProficiencyAllowanceTests
{
    [Fact]
    public void IsPayrollCashAllowance_ExcludesProficiency_KeepsCommission()
    {
        var proficiency = Allowance("PROFICIENCY", 10_000m);
        var commission = Allowance("COMMISSION", 10_000m);
        var medical = Allowance("MED", 8_500m);

        Assert.False(PayrollCalculationService.IsPayrollCashAllowance(proficiency));
        Assert.True(PayrollCalculationService.IsPayrollCashAllowance(commission));
        Assert.True(PayrollCalculationService.IsPayrollCashAllowance(medical));
    }

    [Fact]
    public void Recalculate_CommissionInGeneral_AndAssessment_BothEnterGrossOnce()
    {
        var line = new PayrollLine
        {
            BasicSalary = 94_000m,
            GeneralAllowanceAmount = 10_000m, // COMMISSION only — PROFICIENCY excluded upstream
            MedicalAllowanceAmount = 8_500m,
            TelephoneAllowanceAmount = 4_250m,
            TransportAllowanceAmount = 12_750m,
            NightAllowanceAmount = 5_000m,
            AssessmentAmount = 4_000m
        };

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(40_500m, line.AllowanceAmount);
        Assert.Equal(138_500m, line.GrossPay);
        Assert.Equal(138_500m, line.NetPay);
    }

    [Fact]
    public void Recalculate_WithoutAssessment_DoesNotInventProficiencyCash()
    {
        var line = new PayrollLine
        {
            BasicSalary = 94_000m,
            GeneralAllowanceAmount = 10_000m,
            MedicalAllowanceAmount = 8_500m,
            TelephoneAllowanceAmount = 4_250m,
            TransportAllowanceAmount = 12_750m,
            NightAllowanceAmount = 5_000m,
            AssessmentAmount = 0m
        };

        PayrollCalculationService.Recalculate(line);

        Assert.Equal(134_500m, line.GrossPay);
    }

    private static PayScaleAllowance Allowance(string typeCode, decimal value) => new()
    {
        AllowanceCategory = "GENERAL",
        CalculatedValue = value,
        AllowanceType = new AllowanceType { Code = typeCode, Name = typeCode }
    };
}
