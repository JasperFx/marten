using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchQueryItem
    {
        void Configure(IDocumentSchema schema, NpgsqlCommand command);

        Task Read(DbDataReader reader, IIdentityMap map, CancellationToken token);

        void Read(DbDataReader reader, IIdentityMap map);

    }
}