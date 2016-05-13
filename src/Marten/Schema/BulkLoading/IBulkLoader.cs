using System.Collections.Generic;
using Npgsql;

namespace Marten.Schema.BulkLoading
{
    public interface IBulkLoader<T>
    {
        void Load(ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);
    }
}