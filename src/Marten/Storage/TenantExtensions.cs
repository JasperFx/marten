using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Weasel.Postgresql;
using Marten.Util;
using Npgsql;

namespace Marten.Storage
{
   internal static class TenantExtensions
    {
        internal static IEventStorage EventStorage(this ITenant tenant)
        {
            return (IEventStorage) tenant.StorageFor<IEvent>();
        }

        internal static void RunSql(this ITenant tenant, string sql)
        {
            using var conn = tenant.CreateConnection();
            conn.Open();

            try
            {
                conn.CreateCommand(sql).ExecuteNonQuery();
            }
            finally
            {
                conn.Close();
                conn.Dispose();
            }
        }

        internal static async Task RunSqlAsync(this ITenant tenant, string sql)
        {
            using var conn = tenant.CreateConnection();
            await conn.OpenAsync();

            try
            {
                await conn.CreateCommand(sql).ExecuteNonQueryAsync();
            }
            finally
            {
                await conn.CloseAsync();
                conn.Dispose();
            }
        }

    }
}
