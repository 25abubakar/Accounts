using Accounts.Controllers;
using Accounts.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Accounts.Tests;

public sealed class AttendanceSchedulerApiTests :
    IClassFixture<AccountsWebApplicationFactory>
{
    private readonly AccountsWebApplicationFactory _factory;

    public AttendanceSchedulerApiTests(AccountsWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Evaluate_RejectsMissingSchedulerKey()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/internal/scheduler/attendance/evaluate",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Evaluate_AcceptsValidKeyWithoutAntiforgeryToken()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            AttendanceSchedulerController.ApiKeyHeaderName,
            "test-scheduler-key-that-is-at-least-32-chars");

        var response = await client.PostAsJsonAsync(
            "/api/internal/scheduler/attendance/evaluate",
            new { });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal(0, payload.GetProperty("tenantsFound").GetInt32());
    }

    [Fact]
    public async Task Evaluate_RejectsFuturePakistanDate()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            AttendanceSchedulerController.ApiKeyHeaderName,
            "test-scheduler-key-that-is-at-least-32-chars");

        var response = await client.PostAsJsonAsync(
            "/api/internal/scheduler/attendance/evaluate",
            new
            {
                dateFrom = "2099-01-01",
                dateTo = "2099-01-01"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AssessmentEvaluate_RejectsMissingSchedulerKey()
    {
        using var client = CreateClient();

        var response = await client.PostAsync(
            "/api/internal/scheduler/assessment/evaluate",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AssessmentEvaluate_AcceptsValidSchedulerKey()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add(
            AttendanceSchedulerController.ApiKeyHeaderName,
            "test-scheduler-key-that-is-at-least-32-chars");

        var response = await client.PostAsync(
            "/api/internal/scheduler/assessment/evaluate",
            content: null);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(payload.GetProperty("success").GetBoolean());
        Assert.Equal(0, payload.GetProperty("activeTenants").GetInt32());
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
}
