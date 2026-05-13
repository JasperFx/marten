#nullable enable
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Linq;
using Npgsql;

namespace Marten.Services;

public static class CommandRunnerExtensions
{
    // 9.0: QueryPlan deserialization is internal to Marten, so we use STJ directly
    // rather than routing through the user's ISerializer. This keeps the deserialization
    // independent of the user's serializer choice (and lets QueryPlan drop its
    // Newtonsoft `[JsonProperty]` attributes when Marten core sheds its Newtonsoft dep).
    private static readonly JsonSerializerOptions s_queryPlanJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public static async Task<QueryPlan?> ExplainQueryAsync(this NpgsqlConnection conn, ISerializer serializer, NpgsqlCommand cmd,
        Action<IConfigureExplainExpressions>? configureExplain = null, CancellationToken token = default)
    {
        var config = new ConfigureExplainExpressions();
        configureExplain?.Invoke(config);

        cmd.CommandText = string.Concat($"explain ({config} format json) ", cmd.CommandText);
        cmd.Connection = conn;

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);

        QueryPlanContainer[]? queryPlans = null;
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            await using var stream = await reader.GetFieldValueAsync<System.IO.Stream>(0, token).ConfigureAwait(false);
            queryPlans = await JsonSerializer.DeserializeAsync<QueryPlanContainer[]>(
                stream, s_queryPlanJsonOptions, token).ConfigureAwait(false);
        }

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
