using System;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events;

public class EventQueryMapping: DocumentMapping
{
    public EventQueryMapping(StoreOptions storeOptions): base(typeof(IEvent), storeOptions)
    {
        DatabaseSchemaName = storeOptions.Events.DatabaseSchemaName;

        TenancyStyle = storeOptions.Events.TenancyStyle;

        TableName = new PostgresqlObjectName(DatabaseSchemaName, "mt_events");

        registerQueryableMember(x => x.Sequence, "seq_id");
        if (storeOptions.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            registerQueryableMember(x => x.StreamId, "stream_id");
        }
        else
        {
            registerQueryableMember(x => x.StreamKey, "stream_id");
        }

        registerQueryableMember(x => x.Version, "version");
        registerQueryableMember(x => x.Timestamp, "timestamp");
        registerQueryableMember(x => x.IsArchived, "is_archived");

        registerQueryableMember(x => x.EventTypeName, "type");
        registerQueryableMember(x => x.DotNetTypeName, SchemaConstants.DotNetTypeColumn);


        if (storeOptions.EventGraph.Metadata.CorrelationId.Enabled)
        {
            registerQueryableMember(x => x.CorrelationId, storeOptions.EventGraph.Metadata.CorrelationId.Name);
        }

        if (storeOptions.EventGraph.Metadata.CausationId.Enabled)
        {
            registerQueryableMember(x => x.CausationId, storeOptions.EventGraph.Metadata.CausationId.Name);
        }
    }

    public override DbObjectName TableName { get; }

    private void registerQueryableMember(Expression<Func<IEvent, object>> property, string columnName)
    {
        var member = ReflectionHelper.GetProperty(property);

        var field = DuplicateField(new MemberInfo[] { member }, columnName: columnName);
        QueryMembers.ReplaceMember(member, field);
    }
}
