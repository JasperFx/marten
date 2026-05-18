using Marten.Events;
using Marten.Events.Schema;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class DotNetTypeColumn: MetadataColumn<string>, IEventTableColumn
{
    public DotNetTypeColumn(): base(SchemaConstants.DotNetTypeColumn, x => x.DotNetType)
    {
        AllowNulls = true;
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
