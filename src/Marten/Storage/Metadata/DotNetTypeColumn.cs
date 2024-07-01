using System;
using JasperFx.CodeGeneration;
using Marten.Events;
using Marten.Events.Schema;
using Marten.Internal.CodeGeneration;
using Marten.Schema;

namespace Marten.Storage.Metadata;

internal class DotNetTypeColumn: MetadataColumn<string>, IEventTableColumn
{
    public DotNetTypeColumn(): base(SchemaConstants.DotNetTypeColumn, x => x.DotNetType)
    {
        AllowNulls = true;
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        throw new NotSupportedException();
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        throw new NotSupportedException();
    }

    public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.SetParameterFromMember<IEvent>(index, x => x.DotNetTypeName);
    }

    public string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
