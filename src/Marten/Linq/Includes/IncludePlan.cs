using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Includes;

internal class IncludePlan<T>: IIncludePlan
{
    private readonly Action<T> _callback;
    private readonly IQueryableMember _connectingMember;
    private readonly IQueryableMember? _mappingMember;
    private readonly IDocumentStorage<T> _storage;

    public IncludePlan(IDocumentStorage<T> storage, IQueryableMember connectingMember, Action<T> callback)
        : this(storage, connectingMember, null, callback)
    {

    }

    public IncludePlan(IDocumentStorage<T> storage, IQueryableMember connectingMember, IQueryableMember? mappingMember, Action<T> callback)
    {
        _storage = storage;
        _connectingMember = connectingMember;
        _mappingMember = mappingMember;
        _callback = callback;
    }

    public Type DocumentType => typeof(T);
    public Expression? Where { get; set; }

    public IIncludeReader BuildReader(IMartenSession session)
    {
        var selector = (ISelector<T>)_storage.BuildSelector(session);
        return new IncludeReader<T>(_callback, selector);
    }

    private class WhereFragmentHolder: IWhereFragmentHolder
    {
        public readonly List<ISqlFragment> Wheres = new();

        public void Register(ISqlFragment fragment)
        {
            Wheres.Add(fragment);
        }

        public ISqlFragment BuildWrappedFilter(IDocumentStorage<T> storage, IMartenSession session)
        {
            switch (Wheres.Count)
            {
                case 0:
                    return storage.DefaultWhereFragment();

                case 1:
                    return storage.FilterDocuments(Wheres.Single(), session);

                default:
                    return storage.FilterDocuments(CompoundWhereFragment.And(Wheres), session);
            }
        }
    }

    public void AppendStatement(TemporaryTableStatement tempTable, IMartenSession martenSession,
        ITenantFilter tenantFilter)
    {
        var filters = new WhereFragmentHolder();

        // MemberAccess might leak in from compiled queries, so ignore this expression
        // if it exists because it's really the member of the parent document
        // type that refers to the included documents
        if (Where != null && Where.NodeType != ExpressionType.MemberAccess)
        {
            Expression body = Where;
            if (body is UnaryExpression u) body = u.Operand;
            if (body is LambdaExpression l)
            {
                body = l.Body;
            }


            var parser = new WhereClauseParser(martenSession.Options, _storage.QueryMembers, filters);
            parser.Visit(body);
        }

        var selector = new SelectorStatement { SelectClause = _storage };
        filters.Wheres.Insert(0, new IdInIncludedDocumentIdentifierFilter(tempTable.ExportName, _connectingMember, _mappingMember));

        if (tenantFilter != null)
        {
            filters.Register(tenantFilter);
        }

        var wrapped = filters.BuildWrappedFilter(_storage, martenSession);
        selector.Wheres.Add(wrapped);

        tempTable.AddToEnd(selector);
    }

}
