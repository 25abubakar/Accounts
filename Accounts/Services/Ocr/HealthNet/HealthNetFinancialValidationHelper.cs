using Accounts.Services.Ocr.HealthNet.Models;

namespace Accounts.Services.Ocr.HealthNet
{
    /// <summary>
    /// Independent Health Net financial column validation and claim-total
    /// reconciliation. Each money field has its own source â€” never copy
    /// BilledAmount into Not Allowed / NonCovered or substitute Allowed.
    /// </summary>
    public static class HealthNetFinancialValidationHelper
    {
        public const decimal Tolerance = 0.01m;

        /// <summary>
        /// Expected Total-row money order when all eight cells print.
        /// </summary>
        public static void ApplySequentialTotalAmounts(
            HealthNetClaimTotalsModel result,
            IReadOnlyList<decimal> amounts)
        {
            if (result == null || amounts == null || amounts.Count != 8)
                return;

            result.BilledAmount = amounts[0];
            result.AmountNotAllowed = amounts[1];
            result.AllowedAmount = amounts[2];
            result.CobAllowedAmount = amounts[3];
            result.CobPaidAmount = amounts[4];
            result.PatientCostShareAmount = amounts[5];
            result.PatientResponsibility = amounts[6];
            result.BenefitPayable = amounts[7];
        }

        /// <summary>
        /// Repair geometric mis-banding on sparse Total rows.
        /// Classic failure: Billed CenterX lands in the Not Allowed band,
        /// so AmountNotAllowed is set to the billed value while sequential
        /// column 2 still holds the real Not Allowed amount.
        ///
        /// Only trusts sequential tokens when amounts[0] matches the claim
        /// BilledAmount (Total-row aligned). Otherwise service-line money
        /// words may have leaked into the Y-band collection.
        /// </summary>
        public static void CorrectMisbandedTotalsFromSequence(
            HealthNetClaimTotalsModel result,
            IReadOnlyList<decimal> amounts)
        {
            if (result == null || amounts == null || amounts.Count < 2)
                return;

            var sequentialBilled = amounts[0];
            var sequentialNotAllowed = amounts[1];

            result.BilledAmount ??= sequentialBilled;

            var totalRowAligned =
                result.BilledAmount.HasValue
                && result.BilledAmount.Value == sequentialBilled;

            if (!totalRowAligned)
                return;

            /*
             * Contaminated Not Allowed: equals Billed, but the next Total-row
             * money token is a different amount â€” restore from sequence.
             * Legitimate 100% not-allowed claims keep matching values when
             * sequentialBilled == sequentialNotAllowed.
             */
            if (result.AmountNotAllowed.HasValue
                && result.AmountNotAllowed.Value == result.BilledAmount!.Value
                && sequentialNotAllowed != sequentialBilled)
            {
                result.AmountNotAllowed = sequentialNotAllowed;
            }
            else if (!result.AmountNotAllowed.HasValue)
            {
                result.AmountNotAllowed = sequentialNotAllowed;
            }

            if (amounts.Count < 3)
                return;

            var sequentialAllowed = amounts[2];

            if (result.AllowedAmount == 0.00m
                && sequentialAllowed != 0.00m)
            {
                result.AllowedAmount = sequentialAllowed;
            }
            else if (!result.AllowedAmount.HasValue)
            {
                result.AllowedAmount = sequentialAllowed;
            }

            TryRestoreIfContaminated(
                () => result.CobAllowedAmount,
                v => result.CobAllowedAmount = v,
                amounts,
                3,
                sequentialBilled,
                result.BilledAmount);

            TryRestoreIfContaminated(
                () => result.CobPaidAmount,
                v => result.CobPaidAmount = v,
                amounts,
                4,
                sequentialBilled,
                result.BilledAmount);

            TryRestoreIfContaminated(
                () => result.PatientCostShareAmount,
                v => result.PatientCostShareAmount = v,
                amounts,
                5,
                sequentialBilled,
                result.BilledAmount);

            TryRestoreIfContaminated(
                () => result.PatientResponsibility,
                v => result.PatientResponsibility = v,
                amounts,
                6,
                sequentialBilled,
                result.BilledAmount);

            TryRestoreIfContaminated(
                () => result.BenefitPayable,
                v => result.BenefitPayable = v,
                amounts,
                7,
                sequentialBilled,
                result.BilledAmount);

            ReconcileSparsePtRespAndBenefitTail(result, amounts);
        }

        /// <summary>
        /// Sparse Total/service rows often print "... PtResp, 0.00 Benefit".
        /// Wide Benefit X-bands can place PtResp into BenefitPayable (grid Paid).
        /// </summary>
        public static void ReconcileSparsePtRespAndBenefitTail(
            HealthNetClaimTotalsModel result,
            IReadOnlyList<decimal> sequentialAmounts)
        {
            if (result == null
                || sequentialAmounts == null
                || sequentialAmounts.Count < 2)
            {
                return;
            }

            var benefitToken = sequentialAmounts[^1];
            var ptRespToken = sequentialAmounts[^2];

            if (benefitToken != 0m || ptRespToken <= 0m)
                return;

            if (result.BenefitPayable.HasValue
                && result.BenefitPayable.Value == ptRespToken
                && (!result.PatientResponsibility.HasValue
                    || result.PatientResponsibility.Value == 0m))
            {
                result.PatientResponsibility = ptRespToken;
                result.BenefitPayable = 0m;
                if (!result.PatientCostShareAmount.HasValue)
                    result.PatientCostShareAmount = ptRespToken;
                return;
            }

            if (!result.PatientResponsibility.HasValue
                && result.BenefitPayable.HasValue
                && result.BenefitPayable.Value > 0m
                && result.BenefitPayable.Value == ptRespToken)
            {
                result.PatientResponsibility = ptRespToken;
                result.BenefitPayable = 0m;
            }
        }

        public static void ReconcileLinePtRespAndBenefit(
            ref decimal? patientCostShare,
            ref decimal? patientResponsibility,
            ref decimal? benefitPayable,
            IReadOnlyList<decimal> trailingAmountsLeftToRight)
        {
            if (trailingAmountsLeftToRight == null
                || trailingAmountsLeftToRight.Count < 2)
            {
                // A lone Benefit Payable with blank patient cells remains Paid.
                // Token count cannot override the PDF column mapping.
                return;
            }

            var benefitToken = trailingAmountsLeftToRight[^1];
            var ptRespToken = trailingAmountsLeftToRight[^2];

            if (benefitToken != 0m || ptRespToken <= 0m)
                return;

            if (benefitPayable.HasValue
                && benefitPayable.Value == ptRespToken
                && (!patientResponsibility.HasValue
                    || patientResponsibility.Value == 0m))
            {
                patientResponsibility = ptRespToken;
                benefitPayable = 0m;
                if (!patientCostShare.HasValue)
                    patientCostShare = ptRespToken;
                return;
            }

            if (!patientResponsibility.HasValue
                && benefitPayable.HasValue
                && benefitPayable.Value > 0m
                && benefitPayable.Value == ptRespToken)
            {
                patientResponsibility = ptRespToken;
                benefitPayable = 0m;
            }

            if (!patientCostShare.HasValue
                && patientResponsibility.HasValue
                && patientResponsibility.Value == ptRespToken
                && trailingAmountsLeftToRight.Count >= 3)
            {
                var costShareToken = trailingAmountsLeftToRight[^3];
                if (costShareToken == ptRespToken)
                {
                    patientCostShare = costShareToken;
                }
            }
        }

        /// <summary>
        /// Prefer PDF claim TOTAL. Fill missing fields from independently
        /// summed service lines. Repair Billedâ†’NotAllowed contamination
        /// using the line NonCovered sum when sequence repair is unavailable.
        /// </summary>
        public static void EnsureClaimTotalsFromServiceLines(
            HealthNetClaimModel claim)
        {
            if (claim == null)
                return;

            RepairContaminatedAllowedFromLines(claim);

            claim.Totals ??= new HealthNetClaimTotalsModel();
            if (claim.Totals.HasPrintedTotalRow)
                return; // Preserve every printed Total cell, including blanks and zeros.

            var lines = claim.ServiceLines
                ?? new List<HealthNetClaimLineModel>();

            FillIfMissing(
                () => claim.Totals.BilledAmount,
                v => claim.Totals.BilledAmount = v,
                lines,
                l => l.BilledAmount);

            FillIfMissing(
                () => claim.Totals.AmountNotAllowed,
                v => claim.Totals.AmountNotAllowed = v,
                lines,
                l => l.AmountNotAllowed);

            FillIfMissing(
                () => claim.Totals.AllowedAmount,
                v => claim.Totals.AllowedAmount = v,
                lines,
                l => l.AllowedAmount);

            FillIfMissing(
                () => claim.Totals.CobAllowedAmount,
                v => claim.Totals.CobAllowedAmount = v,
                lines,
                l => l.CobAllowedAmount);

            FillIfMissing(
                () => claim.Totals.CobPaidAmount,
                v => claim.Totals.CobPaidAmount = v,
                lines,
                l => l.CobPaidAmount);

            FillIfMissing(
                () => claim.Totals.PatientCostShareAmount,
                v => claim.Totals.PatientCostShareAmount = v,
                lines,
                l => l.PatientCostShareAmount);

            FillIfMissing(
                () => claim.Totals.BenefitPayable,
                v => claim.Totals.BenefitPayable = v,
                lines,
                l => l.BenefitPayable);

            FillIfMissing(
                () => claim.Totals.PatientResponsibility,
                v => claim.Totals.PatientResponsibility = v,
                lines,
                l => l.PatientResponsibility);

            RepairContaminatedNotAllowedFromLines(claim);
        }

        /// <summary>
        /// Allowed is an independent PDF column. Never derive it from Billed
        /// or Amount Not Allowed. If only the printed claim Total was
        /// contaminated with Billed, restore it from independently parsed
        /// service-line Allowed cells.
        /// </summary>
        public static bool RepairContaminatedAllowedFromLines(
            HealthNetClaimModel claim)
        {
            if (claim?.Totals == null)
                return false;

            var lines = claim.ServiceLines
                ?? new List<HealthNetClaimLineModel>();
            // A line sum is safe only when every service-line Allowed cell
            // was independently read. A partial sum must never replace the
            // PDF Total Allowed value.
            if (lines.Count == 0
                || lines.Any(line => !line.AllowedAmount.HasValue))
            {
                return false;
            }

            var allowedCells = lines
                .Select(line => line.AllowedAmount!.Value)
                .ToList();

            var lineAllowed = allowedCells.Sum();
            var totalAllowed = claim.Totals.AllowedAmount;
            var totalIsMissing = !totalAllowed.HasValue;
            var totalContainsBilled = totalAllowed.HasValue
                && claim.Totals.BilledAmount.HasValue
                && claim.Totals.AmountNotAllowed.HasValue
                && claim.Totals.AmountNotAllowed.Value != 0m
                && AmountsEqual(
                    totalAllowed.Value,
                    claim.Totals.BilledAmount.Value)
                && !AmountsEqual(totalAllowed.Value, lineAllowed);

            if (!totalIsMissing && !totalContainsBilled)
                return false;

#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                $"HN FINANCIAL CHANGE Method={nameof(RepairContaminatedAllowedFromLines)} " +
                $"Field=AllowedAmount Original={totalAllowed?.ToString("0.00") ?? "null"} " +
                $"New={lineAllowed:0.00} Reason=complete independently parsed service-line Allowed cells");
#endif
            claim.Totals.AllowedAmount = lineAllowed;
            return true;
        }
        /// <summary>
        /// When claim Not Allowed equals Billed but service-line NonCovered
        /// sums to a different amount, the Total-row geometry duplicated
        /// Billed into Not Allowed â€” restore from independent line sum.
        /// </summary>
        public static bool RepairContaminatedNotAllowedFromLines(
            HealthNetClaimModel claim)
        {
            if (claim?.Totals == null)
                return false;

            var lines = claim.ServiceLines
                ?? new List<HealthNetClaimLineModel>();

            if (!claim.Totals.AmountNotAllowed.HasValue
                || !claim.Totals.BilledAmount.HasValue
                || claim.Totals.AmountNotAllowed.Value
                   != claim.Totals.BilledAmount.Value
                || !lines.Any(l => l.AmountNotAllowed.HasValue))
            {
                return false;
            }

            var lineNotAllowed = SumIndependent(lines, l => l.AmountNotAllowed);

            if (AmountsEqual(
                    lineNotAllowed,
                    claim.Totals.AmountNotAllowed.Value))
            {
                return false;
            }

            claim.Totals.AmountNotAllowed = lineNotAllowed;
            return true;
        }

        public static HealthNetClaimFinancialSums SumServiceLines(
            IEnumerable<HealthNetClaimLineModel>? lines)
        {
            var list = lines?.ToList()
                ?? new List<HealthNetClaimLineModel>();

            return new HealthNetClaimFinancialSums
            {
                Billed = SumIndependent(list, l => l.BilledAmount),
                NotAllowed = SumIndependent(list, l => l.AmountNotAllowed),
                Allowed = SumIndependent(list, l => l.AllowedAmount),
                CobAllowed = SumIndependent(list, l => l.CobAllowedAmount),
                CobPaid = SumIndependent(list, l => l.CobPaidAmount),
                Coins = SumIndependent(list, l => l.PatientCostShareAmount),
                PatientResponsibility =
                    SumIndependent(list, l => l.PatientResponsibility),
                ProviderPaid = SumIndependent(list, l => l.BenefitPayable),
                HasBilled = list.Any(l => l.BilledAmount.HasValue),
                HasNotAllowed = list.Any(l => l.AmountNotAllowed.HasValue),
                HasAllowed = list.Any(l => l.AllowedAmount.HasValue),
                HasCobAllowed = list.Any(l => l.CobAllowedAmount.HasValue),
                HasCobPaid = list.Any(l => l.CobPaidAmount.HasValue),
                HasCoins = list.Any(l => l.PatientCostShareAmount.HasValue),
                HasPatientResponsibility =
                    list.Any(l => l.PatientResponsibility.HasValue),
                HasProviderPaid = list.Any(l => l.BenefitPayable.HasValue)
            };
        }

        public static List<string> ValidateClaim(
            HealthNetClaimModel claim,
            string? claimLabel = null)
        {
            var warnings = new List<string>();

            if (claim == null)
                return warnings;

            var label = string.IsNullOrWhiteSpace(claimLabel)
                ? (claim.ClaimNo ?? $"index {claim.ClaimIndex}")
                : claimLabel;

            var lineSums = SumServiceLines(claim.ServiceLines);
            var totals = claim.Totals ?? new HealthNetClaimTotalsModel();

            CompareField(
                warnings,
                label,
                "Billed",
                totals.BilledAmount,
                lineSums.Billed,
                lineSums.HasBilled);

            CompareField(
                warnings,
                label,
                "Not Allowed",
                totals.AmountNotAllowed,
                lineSums.NotAllowed,
                lineSums.HasNotAllowed);

            CompareField(
                warnings,
                label,
                "Allowed",
                totals.AllowedAmount,
                lineSums.Allowed,
                lineSums.HasAllowed);

            CompareField(
                warnings,
                label,
                "COB Allowed",
                totals.CobAllowedAmount,
                lineSums.CobAllowed,
                lineSums.HasCobAllowed);

            CompareField(
                warnings,
                label,
                "COB Paid",
                totals.CobPaidAmount,
                lineSums.CobPaid,
                lineSums.HasCobPaid);

            CompareField(
                warnings,
                label,
                "Coins",
                totals.PatientCostShareAmount,
                lineSums.Coins,
                lineSums.HasCoins);

            CompareField(
                warnings,
                label,
                "PT Resp",
                totals.PatientResponsibility,
                lineSums.PatientResponsibility,
                lineSums.HasPatientResponsibility);

            CompareField(
                warnings,
                label,
                "Provider Paid",
                totals.BenefitPayable,
                lineSums.ProviderPaid,
                lineSums.HasProviderPaid);

            /*
             * Diagnostic only â€” never used as a silent replacement rule.
             */
            if (totals.BilledAmount.HasValue
                && totals.AmountNotAllowed.HasValue
                && totals.AllowedAmount.HasValue)
            {
                var derivedAllowed =
                    totals.BilledAmount.Value - totals.AmountNotAllowed.Value;

                if (!AmountsEqual(derivedAllowed, totals.AllowedAmount.Value))
                {
                    warnings.Add(
                        $"Claim {label} diagnostic Billed-NotAllowedâ‰ Allowed: "
                        + $"Billed={totals.BilledAmount:0.00} "
                        + $"NotAllowed={totals.AmountNotAllowed:0.00} "
                        + $"Allowed={totals.AllowedAmount:0.00} "
                        + $"(Billed-NotAllowed={derivedAllowed:0.00}).");
                }
            }

            return warnings;
        }

        public static bool AmountsEqual(decimal a, decimal b) =>
            Math.Abs(a - b) <= Tolerance;

        private static void TryRestoreIfContaminated(
            Func<decimal?> getter,
            Action<decimal?> setter,
            IReadOnlyList<decimal> amounts,
            int index,
            decimal sequentialBilled,
            decimal? claimBilled)
        {
            if (index < 0 || index >= amounts.Count)
                return;

            var sequential = amounts[index];
            var current = getter();

            if (current.HasValue
                && claimBilled.HasValue
                && current.Value == claimBilled.Value
                && sequential != sequentialBilled)
            {
                setter(sequential);
            }
        }

        private static void FillIfMissing(
            Func<decimal?> getter,
            Action<decimal> setter,
            List<HealthNetClaimLineModel> lines,
            Func<HealthNetClaimLineModel, decimal?> selector)
        {
            if (getter().HasValue)
                return;

            if (!lines.Any(l => selector(l).HasValue))
                return;

            setter(SumIndependent(lines, selector));
        }

        private static decimal SumIndependent(
            IEnumerable<HealthNetClaimLineModel> lines,
            Func<HealthNetClaimLineModel, decimal?> selector)
        {
            return lines.Sum(l => selector(l) ?? 0m);
        }

        private static void CompareField(
            List<string> warnings,
            string claimLabel,
            string fieldName,
            decimal? claimTotal,
            decimal lineSum,
            bool linesHaveValue)
        {
            if (!claimTotal.HasValue || !linesHaveValue)
                return;

            if (AmountsEqual(claimTotal.Value, lineSum))
                return;

            warnings.Add(
                $"Claim {claimLabel} {fieldName} cross-check: "
                + $"PDF/total={claimTotal.Value:0.00} vs sum(lines)={lineSum:0.00}. "
                + "Preserving parsed PDF total; service-line sum kept independent.");
        }
    }

    public sealed class HealthNetClaimFinancialSums
    {
        public decimal Billed { get; set; }
        public decimal NotAllowed { get; set; }
        public decimal Allowed { get; set; }
        public decimal CobAllowed { get; set; }
        public decimal CobPaid { get; set; }
        public decimal Coins { get; set; }
        public decimal PatientResponsibility { get; set; }
        public decimal ProviderPaid { get; set; }

        public bool HasBilled { get; set; }
        public bool HasNotAllowed { get; set; }
        public bool HasAllowed { get; set; }
        public bool HasCobAllowed { get; set; }
        public bool HasCobPaid { get; set; }
        public bool HasCoins { get; set; }
        public bool HasPatientResponsibility { get; set; }
        public bool HasProviderPaid { get; set; }
    }
}


