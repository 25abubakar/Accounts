using Accounts.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Accounts.Tests;

public class SecurityPipelineIntegrationTests :
    IClassFixture<AccountsWebApplicationFactory>
{
    private readonly AccountsWebApplicationFactory _factory;

    public SecurityPipelineIntegrationTests(AccountsWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task PublicLogin_ReachesCredentialValidationWithoutAntiforgeryToken()
    {
        using var client = CreateClient();
        var tokenResponse = await client.GetAsync("/api/security/csrf-token");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        var withoutToken = await client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "missing@example.test",
            password = "NotARealPassword!123"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
    }

    [Fact]
    public async Task ValidAntiforgeryToken_AllowsRequestToReachLogin()
    {
        using var client = CreateClient();
        var tokenDocument = await client.GetFromJsonAsync<JsonElement>(
            "/api/security/csrf-token");
        var token = tokenDocument.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = "missing@example.test",
                password = "NotARealPassword!123"
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", token);

        var response = await client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected login to run after antiforgery validation, got {(int)response.StatusCode}: {responseBody}");
    }

    [Fact]
    public async Task Cors_DoesNotAuthorizeUnknownOrigin()
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task DevelopmentHttp_CanIssueAntiforgeryToken()
    {
        using var factory = new AccountsWebApplicationFactory("Development");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var response = await client.GetAsync("/api/security/csrf-token");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("token").GetString()));
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
}
