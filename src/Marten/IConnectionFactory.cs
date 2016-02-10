using System;
using System.Collections.Generic;
using System.Data.Common;
using Baseline;
using Marten.Services;
using Marten.Util;
using Npgsql;

namespace Marten
{
    /// <summary>
    /// Factory interface to customize the construction of an NpgsqlConnection
    /// to the Postgresql database
    /// </summary>
    public interface IConnectionFactory
    {
        /// <summary>
        /// Create a new, isolated connection to the Postgresql database
        /// </summary>
        /// <returns></returns>
        NpgsqlConnection Create();
    }

    public static class ConnectionFactoryExtensions
    {
        public static void RunSql(this IConnectionFactory factory, string sql)
        {
            using (var conn = factory.Create())
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

        private static T execute<T>(this IConnectionFactory factory, Func<NpgsqlConnection, T> func)
        {
            using (var conn = factory.Create())
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

        private static void execute(this IConnectionFactory factory, Action<NpgsqlConnection> action)
        {
            using (var conn = factory.Create())
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

        public static IList<string> GetStringList(this IConnectionFactory factory, string sql, params object[] parameters)
        {
            var list = new List<string>();

            factory.execute(conn =>
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

        public static IEnumerable<T> Fetch<T>(this IConnectionFactory factory, string sql, Func<DbDataReader, T> transform, params object[] parameters)
        {
            return factory.execute(conn =>
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