using System;
using System.Collections.Generic;
using Baseline;
using Marten.Linq;
using Marten.Services.Json;
using Marten.Util;
using Npgsql;

namespace Marten.Services
{
    public static class CommandRunnerExtensions
    {
        public static int Execute(this IManagedConnection runner, string sql)
        {
            var cmd = new NpgsqlCommand(sql);
            return runner.Execute(cmd);
        }

        public static QueryPlan ExplainQuery(this IManagedConnection runner, ISerializer serializer, NpgsqlCommand cmd, Action<IConfigureExplainExpressions> configureExplain = null)
        {
            var config = new ConfigureExplainExpressions();
            configureExplain?.Invoke(config);

            cmd.CommandText = string.Concat($"explain ({config} format json) ", cmd.CommandText);

            using var reader = runner.ExecuteReader(cmd);

            var queryPlans = reader.Read() ? serializer.FromJson<QueryPlanContainer[]>(reader.GetStream(0)) : null;
            var planToReturn = queryPlans?[0].Plan;

            if (planToReturn == null)
                return null;

            planToReturn.PlanningTime = queryPlans[0].PlanningTime;
            planToReturn.ExecutionTime = queryPlans[0].ExecutionTime;
            planToReturn.Command = cmd;

            return planToReturn;
        }


        public static IList<string> GetStringList(this IManagedConnection runner, string sql, params object[] parameters)
        {
            var list = new List<string>();

            var cmd = new NpgsqlCommand();
            cmd.WithText(sql);
            parameters.Each(x =>
            {
                var param = cmd.AddParameter(x);
                cmd.CommandText = cmd.CommandText.UseParameter(param);
            });

            using var reader = runner.ExecuteReader(cmd);

            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }

            reader.Close();

            return list;
        }

        public static T QueryScalar<T>(this IManagedConnection runner, string sql)
        {
            var cmd = new NpgsqlCommand(sql);
            return runner.QueryScalar<T>(cmd);
        }

        public static T QueryScalar<T>(this IManagedConnection runner, NpgsqlCommand cmd)
        {
            using var reader = runner.ExecuteReader(cmd);

            return reader.Read() ? reader.GetFieldValue<T>(0) : default;
        }

    }
}
