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

        public static T Fetch<T>(this IManagedConnection runner, IQueryHandler<T> handler, IIdentityMap map, QueryStatistics stats, ITenant tenant)
        {
            var command = CommandBuilder.ToCommand(tenant, handler);

            return runner.Execute(command, c =>
            {
                using (var reader = command.ExecuteReader())
                {
                    return handler.Handle(reader, map, stats);
                }
            });
        }

        public static async Task<T> FetchAsync<T>(this IManagedConnection runner, IQueryHandler<T> handler, IIdentityMap map, QueryStatistics stats, ITenant tenant, CancellationToken token)
        {
            var command = CommandBuilder.ToCommand(tenant, handler);

            return await runner.ExecuteAsync(command, async (c, tkn) =>
            {
                using (var reader = await command.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                {
                    return await handler.HandleAsync(reader, map, stats, tkn).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
        }

        public static IList<T> Resolve<T>(this IManagedConnection runner, NpgsqlCommand cmd, ISelector<T> selector, IIdentityMap map, QueryStatistics stats)
        {
            var selectMap = map.ForQuery();

            return runner.Execute(cmd, c =>
            {
                var list = new List<T>();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(selector.Resolve(reader, selectMap, stats));
                    }
                }

                return list;
            });
        }

        public static Task<IList<T>> ResolveAsync<T>(this IManagedConnection runner, NpgsqlCommand cmd, ISelector<T> selector, IIdentityMap map, QueryStatistics stats, CancellationToken token)
        {
            var selectMap = map.ForQuery();

            return runner.ExecuteAsync(cmd, async (c, tkn) =>
            {
                var list = new List<T>();
                using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(tkn).ConfigureAwait(false))
                    {
                        list.Add(selector.Resolve(reader, selectMap, stats));
                    }
                }

                return list.As<IList<T>>();
            }, token);
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