#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Linq;
using Npgsql;

namespace Marten.Services;

public static class CommandRunnerExtensions
{
    [Obsolete(QuerySession.SynchronousRemoval)]
    public static QueryPlan? ExplainQuery(this NpgsqlConnection conn, ISerializer serializer, NpgsqlCommand cmd,
        Action<IConfigureExplainExpressions>? configureExplain = null)
    {
        var config = new ConfigureExplainExpressions();
        configureExplain?.Invoke(config);

        cmd.CommandText = string.Concat($"explain ({config} format json) ", cmd.CommandText);
        cmd.Connection = conn;

        using var reader = cmd.ExecuteReader();

        var queryPlans = reader.Read() ? serializer.FromJson<QueryPlanContainer[]>(reader, 0) : null;
        var planToReturn = queryPlans?[0].Plan;

        if (planToReturn == null)
        {
            return null;
        }

        planToReturn.PlanningTime = queryPlans![0].PlanningTime;
        planToReturn.ExecutionTime = queryPlans[0].ExecutionTime;
        planToReturn.Command = cmd;

        return planToReturn;
    }

    public static async Task<QueryPlan?> ExplainQueryAsync(this NpgsqlConnection conn, ISerializer serializer, NpgsqlCommand cmd,
        Action<IConfigureExplainExpressions>? configureExplain = null, CancellationToken token = default)
    {
        var config = new ConfigureExplainExpressions();
        configureExplain?.Invoke(config);

        cmd.CommandText = string.Concat($"explain ({config} format json) ", cmd.CommandText);
        cmd.Connection = conn;

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);

        var queryPlans = await reader.ReadAsync(token).ConfigureAwait(false) ? await serializer.FromJsonAsync<QueryPlanContainer[]>(reader, 0, token).ConfigureAwait(false) : null;
        var planToReturn = queryPlans?[0].Plan;

        if (planToReturn == null)
        {
            return null;
        }

        planToReturn.PlanningTime = queryPlans![0].PlanningTime;
        planToReturn.ExecutionTime = queryPlans[0].ExecutionTime;
        planToReturn.Command = cmd;

        return planToReturn;
    }


    public static T? QueryScalar<T>(this IQuerySession runner, string sql)
    {
        var cmd = new NpgsqlCommand(sql);
        return runner.QueryScalar<T>(cmd);
    }

    public static T? QueryScalar<T>(this IQuerySession runner, NpgsqlCommand cmd)
    {
        using var reader = runner.ExecuteReader(cmd);

        return reader.Read() ? reader.GetFieldValue<T>(0) : default;
    }
}
