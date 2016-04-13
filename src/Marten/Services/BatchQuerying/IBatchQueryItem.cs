using System.Data.Common;
using Marten.Schema;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public interface IBatchQueryItem
    {
        void Configure(IDocumentSchema schema, NpgsqlCommand command);

        // TODO -- THIS REALLY, REALLY needs to be async all the way down
        void Read(DbDataReader reader, IIdentityMap map);


    }
}