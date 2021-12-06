using System;
using System.Collections.Generic;
using Baseline;
using Marten.Linq;
using Weasel.Postgresql;
using Marten.Util;
using Npgsql;
using Weasel.Core;

#nullable enable
namespace Marten.Services
{
    public static class CommandRunnerExtensions
    {
        public static QueryPlan? ExplainQuery(this NpgsqlConnection conn, ISerializer serializer, NpgsqlCommand cmd, Action<IConfigureExplainExpressions>? configureExplain = null)
        {
            var config = new ConfigureExplainExpressions();
            configureExplain?.Invoke(config);

            cmd.CommandText = string.Concat($"explain ({config} format json) ", cmd.CommandText);
            cmd.Connection = conn;

            using var reader = cmd.ExecuteReader();

            var queryPlans = reader.Read() ? serializer.FromJson<QueryPlanContainer[]>(reader, 0) : null;
            var planToReturn = queryPlans?[0].Plan;

            if (planToReturn == null)
                return null;

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
}
