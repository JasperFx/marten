using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using Marten.Events.Projections.Flattened;
using Marten.TimescaleDB.Internal;

namespace Marten.TimescaleDB;

/// <summary>
/// Fluent configuration surface for the TimescaleDB extension, exposed through
/// <see cref="TimescaleDBExtensions.UseTimescaleDB(StoreOptions, System.Action{TimescaleDBOptions})"/>.
/// </summary>
public sealed class TimescaleDBOptions
{
    private readonly StoreOptions _storeOptions;

    internal TimescaleDBOptions(StoreOptions storeOptions)
    {
        _storeOptions = storeOptions;
    }

    internal List<IHypertableTarget> Targets { get; } = new();

    /// <summary>
    /// Turn the flat table written by a registered <see cref="FlatTableProjection"/> into a
    /// TimescaleDB hypertable partitioned by <paramref name="timeColumn"/>.
    ///
    /// The projection's table must either have no primary key/unique constraint, or include
    /// <paramref name="timeColumn"/> in each one — a TimescaleDB requirement for hypertables.
    /// </summary>
    /// <typeparam name="TProjection">The flat-table projection type (must be registered on the store).</typeparam>
    /// <param name="timeColumn">The timestamp column to partition/chunk by.</param>
    /// <param name="configure">Optional configuration of chunk interval, compression, retention, and continuous aggregates.</param>
    public TimescaleDBOptions ProjectionAsHypertable<TProjection>(string timeColumn,
        Action<HypertableOptions>? configure = null)
        where TProjection : FlatTableProjection
    {
        var options = new HypertableOptions(timeColumn);
        configure?.Invoke(options);
        Targets.Add(new ProjectionHypertableTarget(typeof(TProjection), options));
        return this;
    }

    /// <summary>
    /// Turn the table backing document type <typeparamref name="T"/> into a TimescaleDB hypertable
    /// partitioned by the column duplicated from <paramref name="timeColumn"/>.
    ///
    /// TimescaleDB requires the partition column to participate in the primary key, so this
    /// duplicates the selected member into a NOT NULL column and adds it to the document table's
    /// primary key (making it <c>(id, &lt;time&gt;)</c>). The duplicated value therefore must be
    /// immutable for a given document id — best suited to append-heavy types (audit logs, metrics,
    /// activity records) where the timestamp is set once and never changes.
    /// </summary>
    /// <typeparam name="T">The document type.</typeparam>
    /// <param name="timeColumn">A member selecting the timestamp to partition by, e.g. <c>x =&gt; x.CreatedAt</c>.</param>
    /// <param name="configure">Optional configuration of chunk interval, compression, retention, and continuous aggregates.</param>
    public TimescaleDBOptions DocumentAsHypertable<T>(Expression<Func<T, object?>> timeColumn,
        Action<HypertableOptions>? configure = null)
    {
        var columnName = ResolveColumnName(timeColumn);

        // Extension config drives the PK: duplicate the member into a NOT NULL column that is part
        // of the document table's primary key so Marten's own schema model matches the hypertable
        // shape (no drift), and the generated upsert/update/delete SQL picks the composite PK up
        // through the existing partition-aware code paths.
        _storeOptions.Schema.For<T>().Duplicate(timeColumn, notNull: true, partOfPrimaryKey: true);

        var options = new HypertableOptions(columnName);
        configure?.Invoke(options);
        Targets.Add(new DocumentHypertableTarget(typeof(T), options));
        return this;
    }

    private static string ResolveColumnName<T>(Expression<Func<T, object?>> expression)
    {
        var body = expression.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        if (body is MemberExpression memberExpr)
        {
            return memberExpr.Member.Name.ToTableAlias();
        }

        throw new ArgumentException("The time column expression must be a simple member access, e.g. x => x.CreatedAt",
            nameof(expression));
    }
}
