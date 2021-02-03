using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Linq.Parsing;
using Marten.Schema;

namespace Marten.Events
{
    public class EventQueryMapping : DocumentMapping
    {
        public EventQueryMapping(StoreOptions storeOptions) : base(typeof(IEvent), storeOptions)
        {
            DatabaseSchemaName = storeOptions.Events.DatabaseSchemaName;

            TenancyStyle = storeOptions.Events.TenancyStyle;

            TableName = new DbObjectName(DatabaseSchemaName, "mt_events");

            duplicateField(x => x.Sequence, "seq_id");
            if (storeOptions.Events.StreamIdentity == StreamIdentity.AsGuid)
            {
                duplicateField(x => x.StreamId, "stream_id");
            }
            else
            {
                duplicateField(x => x.StreamKey, "stream_id");
            }

            duplicateField(x => x.Version, "version");
            duplicateField(x => x.Timestamp, "timestamp");
        }

        public override DbObjectName TableName { get; }

        private DuplicatedField duplicateField(Expression<Func<IEvent, object>> property, string columnName)
        {
            var finder = new FindMembers();
            finder.Visit(property);

            return DuplicateField(finder.Members.ToArray(), columnName: columnName);
        }

    }
}
