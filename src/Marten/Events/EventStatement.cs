using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events.Archiving;
using Marten.Events.Daemon;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events
{
    internal class EventStatement : Statement
    {
        private const string ALL_TENANTS = "~ALL~";
        private readonly IEventStorage _storage;

        public EventStatement(IEventStorage storage) : base(storage.Fields)
        {
            _storage = storage;
        }

        protected override void configure(CommandBuilder builder)
        {
            _storage.WriteSelectClause(builder);

            var wheres = filters().ToArray();
            switch (wheres.Length)
            {
                case 0:
                    break;
                case 1:
                    builder.Append(" WHERE ");
                    wheres[0].Apply(builder);
                    break;
                default:
                    var where = CompoundWhereFragment.And(wheres);
                    builder.Append(" WHERE ");
                    where.Apply(builder);
                    break;
            }

            builder.Append(" ORDER BY d.seq_id");
            if (Limit > 0)
            {
                builder.Append(" LIMIT ");
                builder.AppendParameter(Limit);
            }
        }

        public ISqlFragment[] Filters { get; set; } = new ISqlFragment[0];

        private IEnumerable<ISqlFragment> filters()
        {
            yield return IsNotArchivedFilter.Instance;

            if (Range != null)
            {
                yield return new WhereFragment("d.seq_id > ?", Range.SequenceFloor);
                yield return new WhereFragment("d.seq_id <= ?", Range.SequenceCeiling);
            }

            if (Version != 0)
            {
                yield return new WhereFragment("d.version <= ?", Version);
            }

            if (FromVersion != 0)
            {
                yield return new WhereFragment("d.version >= ?", FromVersion);
            }

            if (Timestamp.HasValue)
            {
                yield return new WhereFragment("d.timestamp <= ?", Timestamp);
            }

            if (_storage.TenancyStyle == TenancyStyle.Conjoined && TenantId != ALL_TENANTS)
            {
                yield return new WhereFragment("d.tenant_id = ?", TenantId);
            }

            if (StreamId != Guid.Empty)
            {
                yield return new WhereFragment("d.stream_id = ?", StreamId);
            }

            if (StreamKey.IsNotEmpty())
            {
                yield return new WhereFragment("d.stream_id = ?", StreamKey);
            }

            foreach (var filter in Filters)
            {
                yield return filter;
            }
        }

        public EventRange Range { get; set; }

        public long Version { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public string TenantId { get; set; } = ALL_TENANTS;

        public string StreamKey { get; set; }

        public Guid StreamId { get; set; } = Guid.Empty;

        public long FromVersion { get; set; }
    }
}
