using System;
using System.Collections.Generic;
using System.Data.Common;
using Baseline;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
    public static class TenantExtensions
    {
        public static void RunSql(this ITenant tenant, string sql)
        {
            using (var conn = tenant.CreateConnection())
            {
                conn.Open();

                try
                {
                    conn.CreateCommand().WithText(sql).ExecuteNonQuery();
                }
                finally
                {
                    conn.Close();
                    conn.Dispose();
                }
            }
        }

        private static T execute<T>(this ITenant tenant, Func<NpgsqlConnection, T> func)
        {
            using (var conn = tenant.CreateConnection())
            {
                conn.Open();

                try
                {
                    return func(conn);
                }
                finally
                {
                    conn.Close();
                    conn.Dispose();
                }
            }
        }

        private static void execute(this ITenant tenant, Action<NpgsqlConnection> action)
        {
            using (var conn = tenant.CreateConnection())
            {
                conn.Open();

                try
                {
                    action(conn);
                }
                finally
                {
                    conn.Close();
                    conn.Dispose();
                }
            }
        }

        public static IList<string> GetStringList(this ITenant tenant, string sql, params object[] parameters)
        {
            var list = new List<string>();

            tenant.execute(conn =>
            {
                var cmd = conn.CreateCommand().WithText(sql);

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

        public static IEnumerable<T> Fetch<T>(this ITenant tenant, string sql, Func<DbDataReader, T> transform, params object[] parameters)
        {
            return tenant.execute(conn =>
            {
                try
                {
                    return conn.CreateCommand().Fetch(sql, transform, parameters);
                }
                catch (Exception e)
                {
                    throw new Exception($"Error trying to fetch w/ sql '{sql}'", e);
                }
            });
        }
    }
}