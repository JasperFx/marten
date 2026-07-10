#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ImTools;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.Members;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class ChildCollectionMember: QueryableMember, ICollectionMember, IQueryableMemberCollection
{
    private readonly IQueryableMember _count;
    private readonly StoreOptions _options;
    private readonly RootMember _root;
    private ImHashMap<string, IQueryableMember> _members = ImHashMap<string, IQueryableMember>.Empty;
    private readonly string _arraySelector;

    public ChildCollectionMember(StoreOptions options, IQueryableMember parent, Casing casing, MemberInfo member, Type? memberType): base(
        parent, casing, member)
    {
        _options = options;
        TypedLocator = $"{parent.RawLocator} -> '{MemberName}'";
        MemberType ??= memberType;
        ElementType = MemberType.DetermineElementType();

        // I know, this is goofy, but...
        _arraySelector = $"jsonb_array_elements({JSONBLocator})";

        // This to work with GIN indexes
        JSONBLocator = TypedLocator;

        _root = new RootMember(ElementType) { Ancestors = Array.Empty<IQueryableMember>() };

        _count = new CollectionLengthMember(this);

        JsonPathSegment = MemberName + "[*]";

        ArrayLocator = $"CAST(ARRAY(SELECT jsonb_array_elements(CAST({RawLocator} as jsonb))) as jsonb[])";
        ExplodeLocator = $"unnest({ArrayLocator})";

        IsEmpty = new CollectionIsEmpty(this);
        NotEmpty = new CollectionIsNotEmpty(this);
    }

    public ISqlFragment IsEmpty { get; }
    public ISqlFragment NotEmpty { get; }

    public string ArrayLocator { get; set; }

    public Type ElementType { get; }

    public ISqlFragment ParseWhereForAny(Expression body, IReadOnlyStoreOptions options)
    {
        var whereClause = new ChildCollectionWhereClause();
        var parser = new WhereClauseParser((StoreOptions)options, this, whereClause);
        parser.Visit(body);

        return whereClause.CompileFragment(this, options.Serializer());
    }

    public string ExplodeLocator { get; }

    public override void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
    {
        dict[MemberName] = new[] { constant.Value };
    }

    public Statement AttachSelectManyStatement(CollectionUsage collectionUsage, IStorageSession session,
        SelectorStatement parentStatement, QueryStatistics statistics)
    {
        var selectClause =
            typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(parentStatement.ExportName, ElementType);
        return collectionUsage.BuildSelectManyStatement(session, this, selectClause, statistics, parentStatement);
    }

    public ISelectClause BuildSelectClauseForExplosion(string fromObject)
    {
        return typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(fromObject, _arraySelector,
            ElementType);
    }

    public IComparableMember ParseComparableForCount(Expression body)
    {
        if (body is LambdaExpression l)
        {
            body = l.Body;
        }

        var countComparable = new ChildCollectionCount(this, _options.Serializer());
        var parser = new WhereClauseParser(_options, this, countComparable);
        parser.Visit(body);

        return countComparable;
    }

    public ISqlFragment ParseWhereForAll(MethodCallExpression method, IReadOnlyStoreOptions options)
    {
        if (method.Arguments.Last() is LambdaExpression l)
        {
            var body = l.Body;
            var filter = new AllCollectionConditionFilter(this);
            var parser = new WhereClauseParser(_options, this, filter);
            parser.Visit(body);

            // All(pred) == NOT EXISTS an element failing pred, which jsonb_path_exists
            // renders in one scan for any jsonpath-capable predicate. The legacy
            // AllCollectionConditionFilter shapes stay as the fallback (null checks,
            // DateTime values)
            if (filter.Wheres.Count == 1 &&
                JsonPathExistsFilter.TryBuildForAll(filter.Wheres.Single(), this, options.Serializer(),
                    out var jsonPath))
            {
                return jsonPath;
            }

            filter.Compile(method);

            return filter;
        }
        else
        {
            throw new BadLinqExpressionException($"Marten cannot derive a Collection.All() filter for expression '{method}'");
        }


    }

    public ISqlFragment ParseWhereForContains(MethodCallExpression body, IReadOnlyStoreOptions options)
    {
        // Contains(complexObject) is exactly what JSONB containment does best. Note the
        // containment semantics: an element matches when it contains every serialized
        // member of the argument, so documents written with extra/evolved properties
        // still match
        var constant = body.Arguments.Last().ReduceToConstant();
        if (constant.Value() != null)
        {
            return ContainmentWhereFilter.ForValue(this, constant.Value()!, options.Serializer());
        }

        throw new BadLinqExpressionException(
            "Marten does not (yet) support contains queries through collections of element type " +
            ElementType.FullNameInCode());
    }


    public override IQueryableMember FindMember(MemberInfo member)
    {
        if (_members.TryFind(member.Name, out var m))
        {
            return m;
        }

        if (member.Name == "Count" || member.Name == "Length")
        {
            _members = _members.AddOrUpdate(member.Name, _count);
            return _count;
        }

        m = _options.CreateQueryableMember(member, _root);
        _members = _members.AddOrUpdate(member.Name, m);

        return m;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IEnumerator<IQueryableMember> GetEnumerator()
    {
        return _members.Enumerate().Select(x => x.Value).GetEnumerator();
    }
}

internal class AllCollectionConditionFilter: ISubQueryFilter, IWhereFragmentHolder
{
    private ISqlFragment _filter;
    private string _exportName;
    public ICollectionMember Member { get; }

    public AllCollectionConditionFilter(ICollectionMember member)
    {
        Member = member;
    }

    public List<ISqlFragment> Wheres { get; } = new();

    public void Apply(ICommandBuilder builder)
    {
        if (Not)
        {
            builder.Append("NOT(");
        }

        builder.Append("d.ctid in (select ctid from ");
        builder.Append(_exportName);
        builder.Append(")");

        if (Not)
        {
            builder.Append(")");
        }
    }

    /// <summary>
    ///     Psych! Should there be a NOT in front of the sub query
    /// </summary>
    public bool Not { get; set; }

    public ISqlFragment Reverse()
    {
        Not = !Not;
        return this;
    }

    public void PlaceUnnestAbove(IStorageSession session, SelectorStatement statement, ISqlFragment? topLevelWhere = null)
    {
        // First need to unnest the collection into its own recordset
        var unnest = new ExplodeCollectionStatement(session, statement, Member.ArrayLocator) { Where = topLevelWhere };

        // Second, filter the collection
        var filter = new FilterStatement(session, unnest, _filter);

        _exportName = filter.ExportName;
    }

    public void Register(ISqlFragment fragment)
    {
        Wheres.Add(fragment);
    }

    public void Compile(MethodCallExpression methodCallExpression)
    {
        if (Wheres.Count == 1)
        {
            switch (Wheres.Single())
            {
                case MemberComparisonFilter { Right: CommandParameter } filter:
                    _filter = new CompareAllWithinCollectionFilter(filter);
                    return;
                case IsNullFilter nf:
                    _filter = new AllMembersAreNullFilter(nf.Member);
                    return;
                case IsNotNullFilter notnull:
                    _filter = new AllMembersAreNotNullFilter(notnull.Member);
                    return;
            }
        }

        throw new BadLinqExpressionException($"Marten can not (yet) parse the expression '{methodCallExpression}'");
    }
}

internal class CompareAllWithinCollectionFilter: ISqlFragment
{
    private readonly MemberComparisonFilter _inner;

    public CompareAllWithinCollectionFilter(MemberComparisonFilter inner)
    {
        _inner = inner;
    }

    public void Apply(ICommandBuilder builder)
    {
        _inner.Right.Apply(builder);
        builder.Append(" ");
        builder.Append(_inner.Op);

        builder.Append(" ALL (array(select ");
        var locator = _inner.Member.TypedLocator.Replace("d.data", "unnest(data)");
        builder.Append(locator);
        builder.Append("))");
    }
}


internal class AllMembersAreNullFilter: ISqlFragment
{
    private readonly IQueryableMember _member;

    public AllMembersAreNullFilter(IQueryableMember member)
    {
        _member = member;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("true = ALL (select unnest(array(select ");
        var locator = _member.TypedLocator.Replace("d.data", "unnest(data)");
        builder.Append(locator);
        builder.Append(" )) is null)");
    }
}

internal class AllMembersAreNotNullFilter: ISqlFragment
{
    private readonly IQueryableMember _member;

    public AllMembersAreNotNullFilter(IQueryableMember member)
    {
        _member = member;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("true = ALL (array(select ");
        var locator = _member.TypedLocator.Replace("d.data", "unnest(data)");
        builder.Append(locator);
        builder.Append(" )) is not null)");
    }
}

