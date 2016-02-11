using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
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


        public static IEnumerable<T> Resolve<T>(this IManagedConnection runner, NpgsqlCommand cmd, IResolver<T> resolver, IIdentityMap map)
        {
            return runner.Execute(cmd, c =>
            {
                var list = new List<T>();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(resolver.Resolve(reader, map));
                    }
                }

                return list;
            });
        }

        public static async Task<IEnumerable<T>> ResolveAsync<T>(this IManagedConnection runner, NpgsqlCommand cmd, IResolver<T> resolver, IIdentityMap map, CancellationToken token)
        {
            return await runner.ExecuteAsync(cmd, async (c, tkn) =>
            {
                var list = new List<T>();
                using (var reader = await cmd.ExecuteReaderAsync(tkn).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(tkn).ConfigureAwait(false))
                    {
                        list.Add(resolver.Resolve(reader, map));
                    }

                    reader.Close();
                }

                return list;
            }, token).ConfigureAwait(false);
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

        public static async Task<IEnumerable<string>> QueryJsonAsync(this IManagedConnection runner, NpgsqlCommand cmd, CancellationToken token)
        {
            return await runner.ExecuteAsync(cmd, async (c, tkn) =>
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

                return list;
            }, token).ConfigureAwait(false);
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