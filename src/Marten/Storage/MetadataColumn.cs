using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Storage
{
    internal abstract class MetadataColumn: TableColumn
    {
        protected MetadataColumn(string name, string type) : base(name, type)
        {
        }

        public abstract Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token);
        public abstract void Apply(DocumentMetadata metadata, int index, DbDataReader reader);
    }
}
