using Accounts.Models.Scheduler;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json;

namespace Accounts.Services.Scheduler;

public sealed class SchedulerFault(int status,string message,Exception? inner=null) : Exception(message,inner)
{public int Status {get;}=status;}

// The scheduler's only data gateway: parameterized stored procedures, never ad-hoc data SQL.
public sealed class SchedulerSqlStore(SchedulerStorage storage,ILogger<SchedulerSqlStore> logger)
{
    public static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    public static SqlParameter P(string name,object? value)=>value switch
    {
        DateTime time=>new(name,SqlDbType.DateTime2){Value=time},
        byte[] bytes=>new(name,SqlDbType.VarBinary,bytes.Length){Value=bytes},
        string text=>new(name,SqlDbType.NVarChar,-1){Value=text},
        _=>new SqlParameter(name,value??DBNull.Value)
    };
    public static SqlParameter P(string name,object? value,SqlDbType type,int size=0)
    {
        var parameter=size!=0?new SqlParameter(name,type,size):new SqlParameter(name,type);
        parameter.Value=value??DBNull.Value;
        return parameter;
    }

    public async Task<JsonElement> JsonAsync(string procedure,CancellationToken ct,SqlConnection? session=null,params SqlParameter[] parameters)
    {
        var owns=session==null;
        session??=new SqlConnection(storage.SchedulerConnection);
        try
        {
            if(owns)await session.OpenAsync(ct);
            await using var command=new SqlCommand(procedure,session){CommandType=CommandType.StoredProcedure,CommandTimeout=60};
            command.Parameters.AddRange(parameters);
            var value=await command.ExecuteScalarAsync(ct);
            using var document=JsonDocument.Parse(value is null or DBNull?"null":Convert.ToString(value)!);
            return document.RootElement.Clone();
        }
        catch(SqlException error){throw Translate(error,logger);}
        finally{if(owns)await session.DisposeAsync();}
    }
    public async Task ExecuteAsync(string procedure,CancellationToken ct,params SqlParameter[] parameters)
    {
        await using var connection=new SqlConnection(storage.SchedulerConnection);
        try
        {
            await connection.OpenAsync(ct);
            await using var command=new SqlCommand(procedure,connection){CommandType=CommandType.StoredProcedure,CommandTimeout=60};
            command.Parameters.AddRange(parameters);await command.ExecuteNonQueryAsync(ct);
        }
        catch(SqlException error){throw Translate(error,logger);}
    }
    public async Task<bool> AuthorizedAsync(int tenantId,string actor,string route,string action,CancellationToken ct)
    {
        var output=new SqlParameter("@Allowed",SqlDbType.Bit){Direction=ParameterDirection.Output};
        await ExecuteAsync("dbo.usp_Scheduler_Authorize",ct,P("@TenantId",tenantId),P("@Actor",actor),P("@Route",route),P("@Action",action),output);
        return output.Value is bool allowed&&allowed;
    }
    public async Task<DateTime?> NextAsync(Guid? id,int tenantId,string? input,DateTime after,CancellationToken ct)
    {
        // The public calendar contract resolves the job GUID inside SQL, never accepts a foreign row identity.
        var output=new SqlParameter("@NextUtc",SqlDbType.DateTime2){Direction=ParameterDirection.Output};
        await ExecuteAsync("dbo.usp_Scheduler_NextPublic",ct,P("@JobId",id,SqlDbType.UniqueIdentifier),P("@TenantId",tenantId),P("@InputJson",input,SqlDbType.NVarChar,-1),P("@AfterUtc",after),output);
        return output.Value is DateTime value?DateTime.SpecifyKind(value,DateTimeKind.Utc):null;
    }
    public Task<JsonElement> QueryAsync(string operation,int tenantId,Guid? id,CancellationToken ct,bool system=false)
        =>JsonAsync("dbo.usp_Scheduler_Query",ct,null,P("@Operation",operation),P("@TenantId",tenantId),P("@JobId",id,SqlDbType.UniqueIdentifier),P("@Operator",system));
    public async Task InitializeAsync(string databaseName,IEnumerable<ModuleJobInfo> catalog,bool initialize,CancellationToken ct)
    {
        await ExecuteAsync("dbo.usp_Scheduler_Configure",ct,P("@DatabaseName",databaseName),P("@AllowInitialize",initialize));
        await ExecuteAsync("dbo.usp_Scheduler_CatalogSync",ct,P("@CatalogJson",JsonSerializer.Serialize(catalog,JsonOptions)));
    }
    private static SchedulerFault Translate(SqlException error,ILogger logger)
    {
        var status=error.Number switch{50500=>400,50503=>403,50504=>404,50509 or 2601 or 2627 or 1205=>409,_=>500};
        if(status==500)logger.LogError(error,"Scheduler stored procedure failed.");
        return new SchedulerFault(status,status==500?"Scheduler storage is temporarily unavailable.":error.Number is 2601 or 2627?"This scheduler operation already exists.":error.Message,error);
    }
}
