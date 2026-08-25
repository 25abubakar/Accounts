namespace Accounts.Idempotency;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>Zero uses Idempotency:DefaultTtlHours.</summary>
    public int TtlHours { get; init; }

    /// <summary>
    /// Enable only when the business transaction is known to have rolled back on failure.
    /// Financial commands should keep the default false to prevent an unsafe double retry.
    /// </summary>
    public bool ReleaseOnFailure { get; init; }
}
