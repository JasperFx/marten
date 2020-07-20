using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using Marten.Util;
using Npgsql;

namespace Marten.Services
{
    public static class CommandRunnerExtensions
    {
        public static int Execute(this IManagedConnection runner, string sql)
        {
            return runner.Execute(cmd => cmd.WithText(sql).ExecuteNonQuery());
        }

        public static QueryPlan ExplainQuery(this IManagedConnection runner, NpgsqlCommand cmd, Action<IConfigureExplainExpressions> configureExplain = null)
        {
            var serializer = new JsonNetSerializer();

            var config = new ConfigureExplainExpressions();
            configureExplain?.Invoke(config);

            cmd.CommandText = string.Concat($"explain ({config} format json) ", cmd.CommandText);
            return runner.Execute(cmd, c =>
            {
                using (var reader = cmd.ExecuteReader())
                {
                    var queryPlans = reader.Read() ? serializer.FromJson<QueryPlanContainer[]>(reader.GetTextReader(0)) : null;
                    var planToReturn = queryPlans?[0].Plan;
                    if (planToReturn != null)
                    {
                        planToReturn.PlanningTime = queryPlans[0].PlanningTime;
                        planToReturn.ExecutionTime = queryPlans[0].ExecutionTime;
                        planToReturn.Command = cmd;
                    }
                    return planToReturn;
                }
            });
        }


        public static IList<string> GetStringList(this IManagedConnection runner, string sql, params object[] parameters)
        {
            var list = new List<string>();

            runner.Execute(cmd =>
            {
                cmd.WithText(sql);
                parameters.Each(x =>
                {
                    var param = cmd.AddParameter(x);
                    cmd.CommandText = cmd.CommandText.UseParameter(param);
                });

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }
            });

            return list;
        }

        public static T QueryScalar<T>(this IManagedConnection runner, string sql)
        {
            return runner.Execute(cmd => cmd.WithText(sql).ExecuteScalar().As<T>());
        }

        public static Task<T> QueryScalarAsync<T>(this IManagedConnection runner, string sql, CancellationToken token)
        {
            return runner.ExecuteAsync(async (cmd, tkn) =>
            {
                var result = await cmd.WithText(sql).ExecuteScalarAsync(tkn).ConfigureAwait(false);
                return (T)result;
            }, token);
        }
    }
}
