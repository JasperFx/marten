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
    }
}