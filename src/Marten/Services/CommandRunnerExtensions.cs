using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
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


        public static IEnumerable<T> Resolve<T>(this IManagedConnection runner, NpgsqlCommand cmd, ISelector<T> selector, IIdentityMap map)
        {
            var selectMap = map.ForQuery();

            return runner.Execute(cmd, c =>
            {
                var list = new List<T>();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(selector.Resolve(reader, selectMap));
                    }
                }

                return list;
            });
        }

        public static Task<IEnumerable<T>> ResolveAsync<T>(this IManagedConnection runner, NpgsqlCommand cmd, ISelector<T> selector, IIdentityMap map, CancellationToken token)
        {
            var selectMap = map.ForQuery();

            return runner.ExecuteAsync(cmd, async (c, tkn) =>
            {
                var list = new List<T>();
                using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(tkn).ConfigureAwait(false))
                    {
                        list.Add(selector.Resolve(reader, selectMap));
                    }

                    reader.Close();
                }

                return list.AsEnumerable();
            }, token);
        }

        public static IEnumerable<string> QueryJson(this IManagedConnection runner, NpgsqlCommand cmd)
        {
            return runner.Execute(cmd, c =>
            {
                var list = new List<string>();
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }

                return list;
            });
        }

        public static Task<IEnumerable<string>> QueryJsonAsync(this IManagedConnection runner, NpgsqlCommand cmd, CancellationToken token)
        {
            return runner.ExecuteAsync(cmd, async (c, tkn) =>
            {
                var list = new List<string>();
                using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(tkn).ConfigureAwait(false))
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }

                return list.AsEnumerable();
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