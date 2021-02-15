using System.Collections.Generic;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;

namespace Marten.Events.Schema
{
    #region sample_EventsTable
    internal class EventsTable: Table
    {
        public EventsTable(EventGraph events): base(new DbObjectName(events.DatabaseSchemaName, "mt_events"))
        {
            AddPrimaryKey(new EventTableColumn("seq_id", x => x.Sequence));
            AddColumn(new EventTableColumn("id", x => x.Id) {Directive = "NOT NULL"});
            AddColumn(new StreamIdColumn(events));
            AddColumn(new EventTableColumn("version", x => x.Version) {Directive = "NOT NULL"});
            AddColumn<EventJsonDataColumn>();
            AddColumn<EventTypeColumn>();
            AddColumn(new EventTableColumn("timestamp", x => x.Timestamp)
            {
                Directive = "default (now()) NOT NULL", Type = "timestamptz"
            });

            AddColumn<TenantIdColumn>();
            AddColumn(new DotNetTypeColumn {Directive = "NULL"});

            if (events.TenancyStyle == TenancyStyle.Conjoined)
            {
                Constraints.Add(
                    $"FOREIGN KEY(stream_id, {TenantIdColumn.Name}) REFERENCES {events.DatabaseSchemaName}.mt_streams(id, {TenantIdColumn.Name})");
                Constraints.Add(
                    $"CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, {TenantIdColumn.Name}, version)");
            }
            else
            {
                Constraints.Add("CONSTRAINT pk_mt_events_stream_and_version UNIQUE(stream_id, version)");
            }

            Constraints.Add("CONSTRAINT pk_mt_events_id_unique UNIQUE(id)");
        }

        internal IList<IEventTableColumn> SelectColumns()
        {
            var columns = new List<IEventTableColumn>();
            columns.AddRange(Columns.OfType<IEventTableColumn>());

            var data = columns.OfType<EventJsonDataColumn>().Single();
            var typeName = columns.OfType<EventTypeColumn>().Single();
            var dotNetTypeName = columns.OfType<DotNetTypeColumn>().Single();

            columns.Remove(data);
            columns.Insert(0, data);
            columns.Remove(typeName);
            columns.Insert(1, typeName);
            columns.Remove(dotNetTypeName);
            columns.Insert(2, dotNetTypeName);

            return columns;
        }
    }

    #endregion sample_EventsTable
}
