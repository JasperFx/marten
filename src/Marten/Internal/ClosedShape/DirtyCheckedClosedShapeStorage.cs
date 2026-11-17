#nullable enable
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;
using Marten.Storage.Metadata;

namespace Marten.Internal.ClosedShape;

/// <summary>
///     The dirty-tracking storage: Weasel's <see cref="DirtyTrackingClosedShapeDocumentStorage{TDoc,TId}" />
///     (weasel#811, #5733) with Marten's LINQ facet.
/// </summary>
internal sealed class DirtyCheckedClosedShapeStorage<TDoc, TId>: DirtyTrackingClosedShapeDocumentStorage<TDoc, TId>, ILinqDocumentStorage, IHaveMetadataColumns
    where TDoc : notnull
    where TId : notnull
{
    private readonly MartenClosedShapeStorageRules<TDoc, TId> _martenRules;

    public DirtyCheckedClosedShapeStorage(DocumentStorageDescriptor<TDoc, TId> descriptor, MartenClosedShapeStorageRules<TDoc, TId> rules)
        : base(descriptor, rules)
    {
        _martenRules = rules;
    }

    // ---- Marten's LINQ facet (ILinqDocumentStorage), the same on every tracking mode ----

    public IQueryableMemberCollection QueryMembers => _martenRules.QueryMembers;

    public ISelectClause SelectClauseWithDuplicatedFields
        => MartenClosedShapeLinq.SelectClauseWithDuplicatedFields<TDoc>(this);

    void ISqlFragment.Apply(ICommandBuilder builder) => Apply(builder);

    public IQueryHandler<TResult> BuildHandler<TResult>(IStorageSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult : notnull
        => LinqQueryParser.BuildHandler<TDoc, TResult>((ISelector<TDoc>)BuildSelector(session), statement);

    public ISelectClause UseStatistics(QueryStatistics statistics) => new StatsSelectClause<TDoc>(this, statistics);

    MetadataColumn[] IHaveMetadataColumns.MetadataColumns() => _martenRules.MetadataColumns;
}
