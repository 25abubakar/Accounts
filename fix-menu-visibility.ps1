# ══════════════════════════════════════════════════════════════════════════
# FIX MENU VISIBILITY ISSUE
# ══════════════════════════════════════════════════════════════════════════
# 
# PROBLEM: User has permissions but no menus show in sidebar
# CAUSE: MenuPermissions table is empty
# SOLUTION: Link menus to features automatically
#
# USAGE: Run this script from the Accounts root folder
#        .\fix-menu-visibility.ps1
# ══════════════════════════════════════════════════════════════════════════

Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "FIX: Menu Visibility Issue" -ForegroundColor Yellow
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Backend API URL (update if different)
$API_URL = "https://localhost:7015"

Write-Host "Step 1: Checking if backend is running..." -ForegroundColor Cyan

try {
    # Skip SSL certificate validation for localhost
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
    
    $healthCheck = Invoke-RestMethod -Uri "$API_URL/api/health" -Method Get -ErrorAction SilentlyContinue
    Write-Host "✅ Backend is running" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Host "❌ Backend is not running!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please start the backend first:" -ForegroundColor Yellow
    Write-Host "   cd Accounts" -ForegroundColor White
    Write-Host "   dotnet run" -ForegroundColor White
    Write-Host ""
    Write-Host "Press any key to exit..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 1
}

Write-Host "Step 2: Linking menus to features..." -ForegroundColor Cyan

try {
    $response = Invoke-RestMethod -Uri "$API_URL/api/rbac/link-menus-to-features" -Method Post
    
    Write-Host "✅ SUCCESS!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Result:" -ForegroundColor Yellow
    Write-Host "  • Message: $($response.message)" -ForegroundColor White
    Write-Host "  • Menus linked: $($response.count)" -ForegroundColor White
    Write-Host ""
    
    if ($response.count -gt 0) {
        Write-Host "✅ $($response.count) menus are now linked to features" -ForegroundColor Green
        Write-Host ""
        Write-Host "Next Steps:" -ForegroundColor Yellow
        Write-Host "  1. Ask user to logout if already logged in" -ForegroundColor White
        Write-Host "  2. User logs back in" -ForegroundColor White
        Write-Host "  3. Sidebar should now show granted menus" -ForegroundColor White
        Write-Host ""
    }
    else {
        Write-Host "ℹ️  All menus were already linked" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "If user still doesn't see menus, check:" -ForegroundColor Yellow
        Write-Host "  1. User has MENU_* permissions in UserPermissionOverrides table" -ForegroundColor White
        Write-Host "  2. User is hired (has StaffId)" -ForegroundColor White
        Write-Host "  3. Run: SELECT * FROM MenuPermissions (should not be empty)" -ForegroundColor White
        Write-Host ""
    }
}
catch {
    Write-Host "❌ ERROR!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error details:" -ForegroundColor Yellow
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Fallback: Run the SQL script manually:" -ForegroundColor Yellow
    Write-Host "  sqlcmd -S your-server -d your-database -i Accounts\Database\FIX_MENU_PERMISSIONS.sql" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host "Step 3: Verification..." -ForegroundColor Cyan
Write-Host ""
Write-Host "To verify the fix worked, check:" -ForegroundColor Yellow
Write-Host "  • Frontend: User should see menus in sidebar" -ForegroundColor White
Write-Host "  • Backend logs: Should show filtered menu count > 0" -ForegroundColor White
Write-Host "  • Database: SELECT COUNT(*) FROM MenuPermissions (should be > 0)" -ForegroundColor White
Write-Host ""

Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "FIX COMPLETE!" -ForegroundColor Green
Write-Host "════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

Write-Host "Press any key to exit..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
