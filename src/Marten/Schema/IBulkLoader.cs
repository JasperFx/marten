using System.Collections.Generic;
using Npgsql;

namespace Marten.Schema
{
    public interface IBulkLoader<T>
    {
        void Load(NpgsqlConnection conn, IEnumerable<T> documents);
    }
}