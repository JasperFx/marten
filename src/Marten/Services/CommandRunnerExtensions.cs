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
}
