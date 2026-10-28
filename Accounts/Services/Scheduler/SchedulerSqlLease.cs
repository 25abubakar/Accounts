using Microsoft.Data.SqlClient;

namespace Accounts.Services.Scheduler;

// Session-owned SQL locks survive neither process termination nor connection loss.
public sealed class SchedulerSqlLease : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly string resource;
    private SchedulerSqlLease(SqlConnection connection, string resource) { this.connection = connection; this.resource = resource; }
    public static async Task<SchedulerSqlLease?> TryAcquireAsync(string connectionString, string resource, CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@key,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @r;";
            command.Parameters.AddWithValue("@key", resource);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) >= 0) return new(connection,resource);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command=connection.CreateCommand();
            command.CommandText="EXEC sys.sp_releaseapplock @Resource=@key,@LockOwner='Session';";
            command.Parameters.AddWithValue("@key",resource);
            await command.ExecuteNonQueryAsync();
        }
        finally { await connection.DisposeAsync(); }
    }
}
