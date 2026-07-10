#nullable enable
using System;
using System.Linq.Expressions;
using System.Reflection;
using ImTools;
using Marten.Exceptions;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.SqlGeneration.Filters;
using Weasel.Postgresql.SqlGeneration;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Linq.Members;

/// <summary>
///     One complex element of a child collection addressed by position, e.g.
///     x.Children[2] or x.Children.ElementAt(2) — locator "d.data -> 'Children' -> 2".
///     Member access chains through it like any other child document
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members through StoreOptions.CreateQueryableMember; document types are preserved per the AOT publishing guide.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: member creation goes through StoreOptions.CreateQueryableMember (runtime codegen); AOT consumers pre-generate codegen artifacts.")]
internal class ChildDocumentAtIndex: QueryableMember, IQueryableMemberCollection, IComparableMember
{
    private readonly StoreOptions _options;
    private ImHashMap<string, IQueryableMember> _members = ImHashMap<string, IQueryableMember>.Empty;

    public ChildDocumentAtIndex(StoreOptions options, IQueryableMember parent, Casing casing,
        ArrayIndexMember member, Type elementType): base(parent, casing, member)
    {
        _options = options;
        ElementType = elementType;

        RawLocator = TypedLocator = $"{parent.TypedLocator} -> {member.Index}";
        NullTestLocator = RawLocator;
    }

    public Type ElementType { get; }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant == null || constant.Value == null)
        {
            switch (op)
            {
                case "=":
                    return new IsNullFilter(this);

                case "!=":
                    return new IsNotNullFilter(this);
            }
        }

        throw new BadLinqExpressionException(
            $"Marten can only compare an indexed collection element to null directly — query on its members instead, e.g. x.Children[{((ArrayIndexMember)Member).Index}].Name");
    }

    public override IQueryableMember FindMember(MemberInfo member)
    {
        if (_members.TryFind(member.Name, out var m))
        {
            return m;
        }

        m = _options.CreateQueryableMember(member, this);
        _members = _members.AddOrUpdate(member.Name, m);

        return m;
    }

    public System.Collections.Generic.IEnumerator<IQueryableMember> GetEnumerator()
    {
        return System.Linq.Enumerable.Select(_members.Enumerate(), x => x.Value).GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
