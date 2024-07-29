#nullable enable
using Marten.Schema;
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
}
