# API idempotency

Critical commands opt in with `[Idempotent]` and require one UUID in
`X-Idempotency-Key`. The key is scoped to the authenticated user and tenant.

## Runtime outcomes

- A new key acquires a short SQL `SERIALIZABLE` transaction and creates a
  `Processing` record.
- A concurrent request with the same key returns HTTP 409 and `Retry-After`.
- A completed request replays the original status, selected headers, and body,
  with `Idempotency-Replayed: true`.
- Reusing a key with a different method, path, query, content type, or body
  returns HTTP 409.
- Records expire after 24 hours by default and a background service removes
  them.
- The store fails closed. If SQL is unavailable, protected business logic is
  not executed.

## Applying it

```csharp
[ApiController]
[Route("api/payments")]
[Authorize]
public sealed class PaymentsController(IPaymentService payments) : ControllerBase
{
    [HttpPost]
    [Idempotent]
    public async Task<IActionResult> Create(
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var payment = await payments.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = payment.Id }, payment);
    }
}
```

Clients must create a key once per logical operation and reuse that same key
for network retries:

```http
POST /api/payments HTTP/1.1
X-Idempotency-Key: 47e4f652-bf33-45b4-a615-0eacc08664ae
Content-Type: application/json

{"amount": 1250, "currency": "PKR"}
```

The shared React Axios interceptor adds a UUID and preserves a caller-supplied
key. A form should keep its key while submission is pending and replace it only
after success or an intentional new operation. Disabling the submit button is
still recommended; a separately generated key represents a separate command.

## Failure and payment safety

`ReleaseOnFailure` defaults to `false`. A server error or process crash is
kept as an indeterminate record, so a retry cannot accidentally double charge.
Use `[Idempotent(ReleaseOnFailure = true)]` only when the service guarantees
that its database transaction rolled back before the error escaped.

For an internal ledger, also store the idempotency key on the payment/order row
under a unique index and commit that row with the ledger mutation in one
database transaction. For an external payment provider, pass this same key to
the provider and reconcile by that key. The HTTP middleware and an external
charge cannot be made atomic by a Redis or SQL lock alone.

## Scale-out

The current SQL implementation already coordinates multiple API instances.
If throughput later requires Redis, use it only through another
`IIdempotencyStore` implementation with atomic SET-if-absent/Lua operations,
durable completed responses, and fencing tokens. Do not rely on an expiring
Redis/RedLock lease alone for financial exactly-once behavior.

Configuration lives under `Idempotency` in `appsettings.json`. Keep response
retention short, restrict access to the infrastructure table, and encrypt the
database/backups because cached responses may contain business data.
