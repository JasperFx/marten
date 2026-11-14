#nullable enable
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage;

/// <summary>
///     The storage for a registered sub-class in a document hierarchy: Weasel's
///     <see cref="ClosedShapeSubClassDocumentStorage{TDoc,TRoot,TId}" /> (weasel#811, #5733), which delegates
///     everything to the root's storage and narrows the reads to the sub-class, with Marten's rules for
///     the <c>mt_doc_type</c> filter and its LINQ facet.
/// </summary>
internal class SubClassDocumentStorage<T, TRoot, TId>: ClosedShapeSubClassDocumentStorage<T, TRoot, TId>,
    ILinqDocumentStorage, IHaveMetadataColumns
    where T : notnull, TRoot where TId : notnull where TRoot : notnull
{
    private readonly SubClassMapping _mapping;

    public SubClassDocumentStorage(IDocumentStorage<TRoot, TId> parent, SubClassMapping mapping)
        : base(parent, new MartenSubClassRules(mapping))
    {
        _mapping = mapping;
    }

    public IQueryableMemberCollection QueryMembers => _mapping.QueryMembers;

    public ISelectClause SelectClauseWithDuplicatedFields
        => ((ILinqDocumentStorage)Root).SelectClauseWithDuplicatedFields;

    void ISqlFragment.Apply(ICommandBuilder builder) => Apply(builder);

    public IQueryHandler<TResult> BuildHandler<TResult>(IStorageSession session, ISqlFragment statement,
        ISqlFragment currentStatement) where TResult : notnull
        => LinqQueryParser.BuildHandler<T, TResult>((ISelector<T>)BuildSelector(session), statement);

    public ISelectClause UseStatistics(QueryStatistics statistics) => new StatsSelectClause<T>(this, statistics);

    public MetadataColumn[] MetadataColumns() => Root.As<IHaveMetadataColumns>().MetadataColumns();

    /// <summary>
    ///     Marten's sub-class rules: rows are told apart by <c>mt_doc_type</c>, and the soft-delete and
    ///     conjoined-tenancy filters are the sub-class's own rather than layered over the root's.
    /// </summary>
    private sealed class MartenSubClassRules: IClosedShapeSubClassRules<T, TRoot, TId>
    {
        private readonly SubClassMapping _mapping;

        public MartenSubClassRules(SubClassMapping mapping)
        {
            _mapping = mapping;
        }

        // Read on each use: a sub-class registered after this one widens Aliases.
        public Weasel.Core.SqlGeneration.ISqlFragment DocumentTypeFilter => toBasicWhere();

        public string TruncateSql =>
            $"delete from {_mapping.Parent.TableName.QualifiedName} where {SchemaConstants.DocumentTypeColumn} = '{_mapping.Alias}'";

        public Weasel.Core.SqlGeneration.ISqlFragment FilterDocuments(IDocumentStorage<TRoot, TId> root,
            Weasel.Core.SqlGeneration.ISqlFragment query, IStorageSession session)
        {
            var pgQuery = (ISqlFragment)query;
            var extras = extraFilters(pgQuery, session).ToArray();

            return pgQuery.CombineAnd(extras);
        }

        public Weasel.Core.SqlGeneration.ISqlFragment? DefaultWhereFragment(IDocumentStorage<TRoot, TId> root)
        {
            var defaults = defaultFilters().ToArray();
            return defaults.Length switch
            {
                0 => null,
                1 => defaults[0],
                _ => CompoundWhereFragment.And(defaults)
            };
        }

        private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query, IStorageSession session)
        {
            yield return toBasicWhere();

            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete && !query.ContainsAny<ISoftDeletedFilter>())
            {
                yield return ExcludeSoftDeletedFilter.Instance;
            }

            if (_mapping.Parent.TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
            {
                yield return new SpecificTenantFilter(session.TenantId);
            }
        }

        private IEnumerable<ISqlFragment> defaultFilters()
        {
            yield return toBasicWhere();

            if (_mapping.Parent.TenancyStyle == TenancyStyle.Conjoined)
            {
                yield return CurrentTenantFilter.Instance;
            }

            if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
            {
                yield return ExcludeSoftDeletedFilter.Instance;
            }
        }

        private WhereFragment toBasicWhere()
        {
            var aliasValues = _mapping.Aliases.Select(a => $"d.{SchemaConstants.DocumentTypeColumn} = '{a}'").ToArray()
                .Join(" or ");

            var sql = _mapping.Alias.Length > 1 ? $"({aliasValues})" : aliasValues;
            return new WhereFragment(sql);
        }
    }
}
