using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Accounts.Idempotency;
using Accounts.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Accounts.Tests;

public sealed class IdempotencyMiddlewareTests
{
    [Fact]
    public async Task MissingKey_Returns400_WithoutExecutingEndpoint()
    {
        var store = new MemoryStore();
        var middleware = CreateMiddleware(store);
        var context = CreateContext("{}");
        var executed = false;

        await middleware.InvokeAsync(context, _ =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        Assert.False(executed);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task CompletedRequest_IsReplayed_AndBusinessLogicRunsOnce()
    {
        var store = new MemoryStore();
        var middleware = CreateMiddleware(store);
        var key = Guid.NewGuid();
        var executions = 0;

        async Task Execute(HttpContext context)
        {
            executions++;
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""{"paymentId":42}""");
        }

        var first = CreateContext("""{"amount":100}""", key);
        await middleware.InvokeAsync(first, Execute);

        var replay = CreateContext("""{"amount":100}""", key);
        await middleware.InvokeAsync(replay, Execute);

        Assert.Equal(1, executions);
        Assert.Equal(StatusCodes.Status201Created, replay.Response.StatusCode);
        Assert.Equal("true", replay.Response.Headers["Idempotency-Replayed"]);
        Assert.Equal("""{"paymentId":42}""", ReadResponse(replay));
    }

    [Fact]
    public async Task SameKeyWithChangedBody_Returns409()
    {
        var store = new MemoryStore();
        var middleware = CreateMiddleware(store);
        var key = Guid.NewGuid();

        var first = CreateContext("""{"amount":100}""", key);
        await middleware.InvokeAsync(first, context =>
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return context.Response.WriteAsync("{}");
        });

        var mismatch = CreateContext("""{"amount":101}""", key);
        await middleware.InvokeAsync(mismatch, _ => throw new InvalidOperationException());

        Assert.Equal(StatusCodes.Status409Conflict, mismatch.Response.StatusCode);
        Assert.Contains("payload mismatch", ReadResponse(mismatch), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CanceledPassThroughRequest_DoesNotBubbleOperationCanceledException()
    {
        var store = new MemoryStore();
        var middleware = CreateMiddleware(store);
        var context = CreateContext("{}");
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "pass-through"));
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;

        await middleware.InvokeAsync(context, _ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });

        Assert.Equal(0, store.FailureCount);
    }

    [Fact]
    public async Task CanceledIdempotentExecution_IsNotReleasedForUnsafeRetry()
    {
        var store = new MemoryStore();
        var middleware = CreateMiddleware(store);
        var context = CreateContext("{}", Guid.NewGuid());
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;

        await middleware.InvokeAsync(context, _ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        });

        Assert.Equal(1, store.FailureCount);
        Assert.False(store.LastFailureRelease);
    }

    private static IdempotencyMiddleware CreateMiddleware(IIdempotencyStore store) =>
        new(
            store,
            new TestTenantService(),
            TimeProvider.System,
            Options.Create(new IdempotencyOptions()),
            NullLogger<IdempotencyMiddleware>.Instance);

    private static DefaultHttpContext CreateContext(string body, Guid? key = null)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "test-user")],
            "Test"));
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/payments";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Response.Body = new MemoryStream();
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new IdempotentAttribute()),
            "test"));
        if (key.HasValue)
            context.Request.Headers["X-Idempotency-Key"] = key.Value.ToString("D");
        return context;
    }

    private static string ReadResponse(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    private sealed class TestTenantService : ITenantService
    {
        public int? TenantId => 7;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => false;
        public int RequiredTenantId => 7;
    }

    private sealed class MemoryStore : IIdempotencyStore
    {
        private readonly Dictionary<string, Entry> _entries = [];

        public int FailureCount { get; private set; }
        public bool? LastFailureRelease { get; private set; }

        public Task<IdempotencyBeginResult> BeginAsync(
            IdempotencyCommand command,
            CancellationToken cancellationToken)
        {
            var dictionaryKey = $"{Convert.ToHexString(command.ScopeHash)}:{command.Key:D}";
            if (!_entries.TryGetValue(dictionaryKey, out var entry))
            {
                var id = Guid.NewGuid();
                var token = Guid.NewGuid();
                _entries[dictionaryKey] = new(command.RequestHash, id, token, null);
                return Task.FromResult(new IdempotencyBeginResult(
                    IdempotencyBeginState.Acquired,
                    id,
                    token,
                    command.LeaseExpiresUtc));
            }

            if (!CryptographicOperations.FixedTimeEquals(entry.RequestHash, command.RequestHash))
                return Task.FromResult(new IdempotencyBeginResult(IdempotencyBeginState.PayloadMismatch));

            return Task.FromResult(entry.Response is null
                ? new IdempotencyBeginResult(IdempotencyBeginState.Processing, entry.Id)
                : new IdempotencyBeginResult(
                    IdempotencyBeginState.Completed,
                    entry.Id,
                    Response: entry.Response));
        }

        public Task CompleteAsync(
            Guid recordId,
            Guid lockToken,
            StoredIdempotencyResponse response,
            DateTime completedUtc,
            CancellationToken cancellationToken)
        {
            var pair = _entries.Single(item => item.Value.Id == recordId);
            _entries[pair.Key] = pair.Value with { Response = response };
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid recordId,
            Guid lockToken,
            string reason,
            bool release,
            DateTime failedUtc,
            CancellationToken cancellationToken)
        {
            FailureCount++;
            LastFailureRelease = release;
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredAsync(DateTime utcNow, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        private sealed record Entry(
            byte[] RequestHash,
            Guid Id,
            Guid Token,
            StoredIdempotencyResponse? Response);
    }
}
