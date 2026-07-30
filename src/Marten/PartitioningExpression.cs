#nullable enable
using System;
using System.Linq;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Weasel.Core.Partitioning;
using Weasel.Postgresql.Tables.Partitioning;

namespace Marten;

public class PartitioningExpression
{
    private readonly DocumentMapping _mapping;
    private readonly string[] _columnNames;

    public PartitioningExpression(DocumentMapping mapping, string[] columnNames)
    {
        _mapping = mapping;
        _columnNames = columnNames;
    }

    /// <summary>
    /// Direct Marten to use PostgreSQL HASH-based partitioning, but to allow the partitions to be managed
    /// externally from Marten
    /// </summary>
    public void  ByExternallyManagedHashPartitions()
    {
        _mapping.IgnorePartitions = true;
        var partitioning = new HashPartitioning { Columns = _columnNames };
        _mapping.Partitioning = partitioning;
    }

    /// <summary>
    /// Direct Marten to apply equally sized PostgreSQL HASH-based partitioning with a partition for
    /// each named partition suffix
    /// </summary>
    /// <param name="suffixes"></param>
    /// <returns></returns>
    public HashPartitioning ByHash(params string[] suffixes)
    {
        var partitioning = new HashPartitioning { Columns = _columnNames, Suffixes = suffixes };
        _mapping.Partitioning = partitioning;

        return partitioning;
    }

    /// <summary>
    /// Direct Marten to use PostgreSQL LIST partitioning, but to allow the partitions to be managed
    /// externally from Marten
    /// </summary>
    public void ByExternallyManagedListPartitions()
    {
        _mapping.IgnorePartitions = true;
        var partitioning = new ListPartitioning { Columns = _columnNames };
        _mapping.Partitioning = partitioning;
    }

    /// <summary>
    /// Direct Marten to use PostgreSQL LIST partitioning with Marten explicitly controlling the
    /// table partitions
    /// </summary>
    /// <returns></returns>
    public ListPartitioning ByList()
    {
        var partitioning = new ListPartitioning { Columns = _columnNames };
        _mapping.Partitioning = partitioning;

        return partitioning;
    }

    /// <summary>
    /// Direct Marten to use PostgreSQL RANGE partitioning, but to allow the partitions to be managed
    /// externally from Marten
    /// </summary>
    public void ByExternallyManagedRangePartitions()
    {
        _mapping.IgnorePartitions = true;
        var partitioning = new RangePartitioning { Columns = _columnNames };
        _mapping.Partitioning = partitioning;
    }

    /// <summary>
    /// Direct Marten to use PostgreSQL RANGE partitioning with Marten explicitly controlling the
    /// table partitions
    /// </summary>
    /// <returns></returns>
    public RangePartitioning ByRange()
    {
        var partitioning = new RangePartitioning { Columns = _columnNames };
        _mapping.Partitioning = partitioning;

        return partitioning;
    }

    /// <summary>
    /// Direct Marten to use PostgreSQL RANGE partitioning over a *rolling time window* that Marten itself
    /// owns: it provisions the periods at the leading edge and drops the aged periods at the trailing edge
    /// on the same schedule it applies every other schema change. This is the supported way to run a
    /// time-series document table -- retention becomes a <c>DROP TABLE</c> of one partition (O(1), no mass
    /// <c>DELETE</c>, no bloat, no vacuum storm) without giving up Weasel's schema ordering and dependency
    /// management the way <see cref="ByExternallyManagedRangePartitions"/> does.
    /// <para>
    /// The partitioned member must be a date/time member that has also been duplicated into a real column
    /// (<c>Duplicate(x => x.Timestamp)</c>), and the window is always computed in UTC.
    /// </para>
    /// </summary>
    /// <param name="period">The size of a single partition -- hour, day, week, month or year.</param>
    /// <param name="periodsAhead">
    /// How many periods beyond the current one to provision. At least one is strongly recommended so that
    /// rows written at the very end of a period always have a partition waiting for them.
    /// </param>
    /// <param name="periodsBehind">
    /// How many completed periods to retain. Partitions older than this are dropped by the retention pass.
    /// </param>
    /// <param name="timeProvider">Clock used to resolve "now". Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <seealso href="https://github.com/JasperFx/marten/issues/5093"/>
    public ManagedRangePartitions ByRollingRange(PartitionPeriod period, int periodsAhead, int periodsBehind,
        TimeProvider? timeProvider = null)
        => ByRollingRange(new RollingWindowPolicy(period, periodsAhead, periodsBehind), timeProvider);

    /// <summary>
    /// Direct Marten to use PostgreSQL RANGE partitioning over a rolling time window described by
    /// <paramref name="policy"/>. See <see cref="ByRollingRange(PartitionPeriod,int,int,TimeProvider)"/>.
    /// </summary>
    public ManagedRangePartitions ByRollingRange(RollingWindowPolicy policy, TimeProvider? timeProvider = null)
        => ByRollingRange(new ManagedRangePartitions(policy, timeProvider));

    /// <summary>
    /// Direct Marten to use PostgreSQL RANGE partitioning over a rolling time window owned by a
    /// pre-built <see cref="ManagedRangePartitions"/>. Pass the *same* manager instance to several
    /// document types to roll every one of their tables forward in a single pass.
    /// </summary>
    public ManagedRangePartitions ByRollingRange(ManagedRangePartitions partitions)
    {
        ArgumentNullException.ThrowIfNull(partitions);

        assertPartitionedOnATimestamp(partitions.Policy);

        var partitioning = new RangePartitioning { Columns = _columnNames }.UsePartitionManager(partitions);
        _mapping.Partitioning = partitioning;

        return partitions;
    }

    /// <summary>
    /// A rolling window is a function of the clock, so the partition key has to actually be a point in time.
    /// Failing here -- at configuration -- turns what would otherwise surface as an opaque PostgreSQL
    /// "partition bound" error during the first migration into a message that names the member.
    /// </summary>
    private void assertPartitionedOnATimestamp(RollingWindowPolicy policy)
    {
        if (_columnNames.Length != 1)
        {
            throw new InvalidOperationException(
                $"A rolling range partition ({policy}) is keyed on a single date/time member, but {_mapping.DocumentType.FullNameInCode()} was partitioned on {_columnNames.Length} columns ({string.Join(", ", _columnNames)}).");
        }

        var columnName = _columnNames[0];
        var field = _mapping.DuplicatedFields.FirstOrDefault(x => x.ColumnName == columnName);

        if (field == null)
        {
            throw new InvalidOperationException(
                $"A rolling range partition ({policy}) has to be keyed on a duplicated date/time member of {_mapping.DocumentType.FullNameInCode()}, but there is no duplicated field for column '{columnName}'. Add a Duplicate(x => x.{columnName}) call for the partitioned member.");
        }

        var memberType = Nullable.GetUnderlyingType(field.MemberType) ?? field.MemberType;
        if (memberType != typeof(DateTimeOffset) && memberType != typeof(DateTime))
        {
            throw new InvalidOperationException(
                $"A rolling range partition ({policy}) has to be keyed on a DateTime or DateTimeOffset member, but {_mapping.DocumentType.FullNameInCode()}.{field.MemberName} is {memberType.FullNameInCode()}.");
        }
    }
}
