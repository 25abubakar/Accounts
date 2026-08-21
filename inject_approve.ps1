
$content = Get-Content Accounts\Controllers\AttendanceController.cs -Raw
$newEndpoint = @"
    [HttpPost("deductions/adjustment/approve")]
    public async Task<IActionResult> ApproveAdjustment([FromBody] ApproveAdjustmentRequestDto dto, CancellationToken ct)
    {
        if (!_tenant.TenantId.HasValue) return Forbid();
        if (!await HasAttendanceMenuActionAsync("EDIT", ct, "/attendance/deduction"))
            return Forbid();

        var validCode = await _db.ProcessApprovalCodes.FirstOrDefaultAsync(x => x.TenantId == _tenant.RequiredTenantId && x.ProcessName == "DeductionAdjustment", ct);
        if (validCode == null || validCode.PinCode != dto.PinCode)
        {
            return BadRequest(new { message = "Invalid approval code." });
        }

        var record = await _db.AttendanceMonthlySettlements
            .FirstOrDefaultAsync(s => s.PersonId == dto.PersonId 
                                      && s.SettlementYear == dto.Year 
                                      && s.SettlementMonth == dto.Month
                                      && s.TenantId == _tenant.RequiredTenantId, ct);

        if (record == null)
            return NotFound(new { message = "Adjustment record not found." });

        if (record.IsAdjustmentApproved)
            return BadRequest(new { message = "Already approved." });

        record.IsAdjustmentApproved = true;
        record.ApprovedByUserId = _currentUser.UserId;
        record.ApprovedDateUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Adjustment approved successfully." });
    }
"@
$content = $content -replace '(?s)(public async Task<IActionResult> SaveAdjustment.*?return Ok\(new \{ message = "Adjustment saved successfully." \}\);\s*\})', "`$1`n`n$newEndpoint"
Set-Content Accounts\Controllers\AttendanceController.cs -Value $content

