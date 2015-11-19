using Marten.Schema;
using Npgsql;

namespace Marten.Map
{
    public abstract class DocumentChange
    {
        public abstract NpgsqlCommand CreateCommand(IDocumentSchema schema);
    }
}