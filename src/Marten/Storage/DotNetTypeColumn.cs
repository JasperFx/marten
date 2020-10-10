using Marten.Schema;

namespace Marten.Storage
{
    internal class DotNetTypeColumn: MetadataColumn<string>
    {
        public DotNetTypeColumn(): base(SchemaConstants.DotNetTypeColumn, x => x.DotNetType)
        {
            CanAdd = true;
        }
    }
}
