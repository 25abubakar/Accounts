using System.Security.Claims;
using Accounts.Controllers;
using Accounts.Data;
using Accounts.Models;
using Accounts.Services.Interfaces;
using Accounts.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Tests;

public sealed class PlatformSettingsControllerTests
{
    private sealed class TenantContext(int? id, bool superAdmin = false) : ITenantService
    {
        public int? TenantId => id;
        public bool IsSuperAdmin => superAdmin;
        public bool IsTenantAdmin => !superAdmin;
        public int RequiredTenantId => id ?? throw new InvalidOperationException();
    }

    private static PlatformSettingsController Controller(ApplicationDbContext db, ITenantService tenant, string role)
        => new(db, tenant)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.NameIdentifier, "configuration-test"),
                        new Claim(ClaimTypes.Role, role)
                    ], "test"))
                }
            }
        };

    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("Staff")]
    public async Task NonSuperAdminsCannotMutateAnyConfiguration(string role)
    {
        var tenant = new TenantContext(3);
        await using var db = TestDbFactory.Create(tenant);
        var api = Controller(db, tenant, role);
        var name = new SaveNamedSettingDto { Name = "Forbidden" };
        Assert.IsType<ForbidResult>(await api.CreateAction(name, default));
        Assert.IsType<ForbidResult>(await api.UpdateAction(1, name, default));
        Assert.IsType<ForbidResult>(await api.DeleteAction(1, default));
        Assert.IsType<ForbidResult>(await api.CreateProcess(name, default));
        Assert.IsType<ForbidResult>(await api.UpdateProcess(1, name, default));
        Assert.IsType<ForbidResult>(await api.DeleteProcess(1, default));
        Assert.IsType<ForbidResult>(await api.CreateStatus(name, default));
        Assert.IsType<ForbidResult>(await api.UpdateStatus(1, name, default));
        Assert.IsType<ForbidResult>(await api.DeleteStatus(1, default));
        Assert.IsType<ForbidResult>(await api.CreateColor(new(), default));
        Assert.IsType<ForbidResult>(await api.UpdateColor(1, new(), default));
        Assert.IsType<ForbidResult>(await api.DeleteColor(1, default));
        Assert.IsType<ForbidResult>(await api.CreateProcessStatus(new(), default));
        Assert.IsType<ForbidResult>(await api.UpdateProcessStatus(1, new(), default));
        Assert.IsType<ForbidResult>(await api.DeleteProcessStatus(1, default));
        Assert.IsType<ForbidResult>(await api.CreateActionStatus(new(), default));
        Assert.IsType<ForbidResult>(await api.UpdateActionStatus(1, new(), default));
        Assert.IsType<ForbidResult>(await api.DeleteActionStatus(1, default));
        Assert.IsType<ForbidResult>(await api.CreateStatusCrDbValue(new(), default));
        Assert.IsType<ForbidResult>(await api.UpdateStatusCrDbValue(1, new(), default));
        Assert.IsType<ForbidResult>(await api.DeleteStatusCrDbValue(1, default));
    }

    [Fact]
    public async Task SuperAdminSavesMappingAndCompaniesReadSameGlobalData()
    {
        var adminTenant = new TenantContext(null, true);
        await using var db = TestDbFactory.Create(adminTenant);
        var admin = Controller(db, adminTenant, "SuperAdmin");
        Assert.IsType<OkObjectResult>(await admin.CreateAction(new() { Name = "Created" }, default));
        Assert.IsType<OkObjectResult>(await admin.CreateProcess(new() { Name = "Daily Activities" }, default));
        Assert.IsType<OkObjectResult>(await admin.CreateStatus(new() { Name = "Holiday" }, default));
        Assert.IsType<OkObjectResult>(await admin.CreateColor(new() { ColorCode = "c0062c", FontColor = "ffffff" }, default));
        var process = await db.PlatformSettingProcesses.SingleAsync();
        var status = await db.PlatformSettingStatuses.SingleAsync();
        var color = await db.PlatformSettingColors.SingleAsync();
        var payload = new SaveProcessStatusDto { ProcessId = process.Id, StatusId = status.Id, ColorId = color.Id, Value = "H" };
        var saved = Assert.IsType<ProcessStatusSettingDto>(Assert.IsType<OkObjectResult>(await admin.CreateProcessStatus(payload, default)).Value);
        Assert.Equal("#C0062C", saved.ColorCode);
        Assert.IsType<ConflictObjectResult>(await admin.CreateProcessStatus(payload, default));
        Assert.IsType<ConflictObjectResult>(await admin.DeleteProcess(process.Id, default));
        Assert.IsType<ConflictObjectResult>(await admin.DeleteStatus(status.Id, default));
        Assert.IsType<ConflictObjectResult>(await admin.DeleteColor(color.Id, default));
        db.ChangeTracker.Clear();
        foreach (var tenantId in new[] { 3, 99 })
        {
            var tenantApi = Controller(db, new TenantContext(tenantId), "TenantAdmin");
            var data = Assert.IsType<PlatformSettingsDto>(Assert.IsType<OkObjectResult>(await tenantApi.Get(default)).Value);
            Assert.Equal(saved, Assert.Single(data.ProcessStatuses));
            Assert.Single(data.Actions);
            var lookup = Assert.IsType<List<ProcessStatusSettingDto>>(
                Assert.IsType<OkObjectResult>(await tenantApi.GetProcessStatuses(process.Id, default)).Value);
            Assert.Equal(saved, Assert.Single(lookup));
            Assert.IsType<BadRequestObjectResult>(await tenantApi.GetProcessStatuses(0, default));
            Assert.IsType<NotFoundObjectResult>(await tenantApi.GetProcessStatuses(999999, default));
        }
        payload.Value = "Updated";
        Assert.IsType<OkObjectResult>(await admin.UpdateProcessStatus(saved.Id, payload, default));
        db.ChangeTracker.Clear();
        Assert.Equal("Updated", (await db.PlatformSettingProcessStatuses.SingleAsync()).Value);
        Assert.IsType<OkObjectResult>(await admin.DeleteProcessStatus(saved.Id, default));
        Assert.Empty(await db.PlatformSettingProcessStatuses.ToListAsync());
    }
}
