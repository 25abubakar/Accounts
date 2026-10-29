using Microsoft.Data.SqlClient;
using System.Data;

namespace Accounts.Services.Scheduler;

// Session-owned SQL locks survive neither process termination nor connection loss.
public sealed class SchedulerSqlLease : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly string resource;
    internal SqlConnection Connection=>connection;
    private SchedulerSqlLease(SqlConnection connection, string resource) { this.connection = connection; this.resource = resource; }
    public static async Task<SchedulerSqlLease?> TryAcquireAsync(string connectionString, string resource, CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "dbo.usp_Scheduler_Lease";command.CommandType=CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource", resource);
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
            command.CommandText="dbo.usp_Scheduler_Lease";command.CommandType=CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@Resource",resource);command.Parameters.AddWithValue("@Release",true);
            await command.ExecuteScalarAsync();
        }
        finally { await connection.DisposeAsync(); }
    }
}
