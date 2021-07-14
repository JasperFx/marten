using System.Collections.Generic;
using System.Linq;
using Marten.Events.Archiving;
using Weasel.Postgresql;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using IndexDefinition = Weasel.Postgresql.Tables.IndexDefinition;

namespace Marten.Events.Schema
{
    #region sample_EventsTable
    internal class EventsTable: Table
    {
        public EventsTable(EventGraph events): base(new DbObjectName(events.DatabaseSchemaName, "mt_events"))
        {
            AddColumn(new EventTableColumn("seq_id", x => x.Sequence)).AsPrimaryKey();
            AddColumn(new EventTableColumn("id", x => x.Id)).NotNull();
            AddColumn(new StreamIdColumn(events));

            AddColumn(new EventTableColumn("version", x => x.Version)).NotNull();
            AddColumn<EventJsonDataColumn>();
            AddColumn<EventTypeColumn>();
            AddColumn(new EventTableColumn("timestamp", x => x.Timestamp))
                .NotNull().DefaultValueByString("(now())");

            AddColumn<TenantIdColumn>();

            AddColumn<DotNetTypeColumn>().AllowNulls();

            AddIfActive(events.Metadata.CorrelationId);
            AddIfActive(events.Metadata.CausationId);
            AddIfActive(events.Metadata.Headers);



            if (events.TenancyStyle == TenancyStyle.Conjoined)
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_tenant_id")
                {
                    ColumnNames = new string[]{"stream_id", TenantIdColumn.Name},
                    LinkedNames = new string[]{"id", TenantIdColumn.Name},
                    LinkedTable = new DbObjectName(events.DatabaseSchemaName, "mt_streams")
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true,
                    Columns = new string[]{"stream_id", TenantIdColumn.Name, "version"}
                });
            }
            else
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id")
                {
                    ColumnNames = new string[]{"stream_id"},
                    LinkedNames = new string[]{"id"},
                    LinkedTable = new DbObjectName(events.DatabaseSchemaName, "mt_streams"),
                    OnDelete = CascadeAction.Cascade
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true,
                    Columns = new string[]{"stream_id", "version"}
                });
            }

            Indexes.Add(new IndexDefinition("pk_mt_events_id_unique")
            {
                Columns = new string[]{"id"},
                IsUnique = true
            });

            AddColumn<IsArchivedColumn>();
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

        private void AddIfActive(MetadataColumn column)
        {
            if (column.Enabled)
            {
                AddColumn(column);
            }
        }
    }

    #endregion sample_EventsTable
}
