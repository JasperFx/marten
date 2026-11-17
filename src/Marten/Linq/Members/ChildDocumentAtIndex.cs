#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using ImTools;
using JasperFx.Core.Reflection;
using Marten.Linq.Parsing;
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
internal class ChildDocumentAtIndex: QueryableMember, IQueryableMemberCollection, IComparableMember
{
    private readonly StoreOptions _options;
    private ImHashMap<string, IQueryableMember> _members = ImHashMap<string, IQueryableMember>.Empty;

    public ChildDocumentAtIndex(StoreOptions options, IQueryableMember parent, Casing casing,
        ArrayIndexMember member, Type elementType): base(parent, casing, member)
    {
        _options = options;
        ElementType = elementType;

        // #5949: what Select(x => x.Children[0]) deserializes the element into
        MemberType = elementType;

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
        if (member is ArrayIndexMember index)
        {
            return findIndexedElement(index);
        }

        if (_members.TryFind(member.Name, out var m))
        {
            return m;
        }

        m = _options.CreateQueryableMember(member, this);
        _members = _members.AddOrUpdate(member.Name, m);

        return m;
    }

    /// <summary>
    ///     #5949: this element is itself an array, <c>x.Grid[1]</c>, and is indexed again, <c>x.Grid[1][0]</c>.
    /// </summary>
    private IQueryableMember findIndexedElement(ArrayIndexMember index)
    {
        var key = $"ArrayIndex[{index.Index}]";
        if (_members.TryFind(key, out var indexed))
        {
            return indexed;
        }

        var innerType = ElementType == typeof(string) ? null : ElementType.DetermineElementType();
        if (innerType == null || ElementType.Closes(typeof(IDictionary<,>)))
        {
            throw new BadLinqExpressionException(
                $"Marten cannot index into an element of type {ElementType.FullNameInCode()}");
        }

        // RawLocator is the jsonb element itself, `d.data -> 'grid' -> 1`, so ->> reads its scalar elements as
        // text and -> its complex ones as jsonb, exactly as one level down from the document
        if (innerType == typeof(string))
        {
            indexed = StringMember.ForIndexedElement(this, RawLocator, index);
        }
        else if (innerType.IsValueTypeForQuerying())
        {
            indexed = SimpleCastMember.ForIndexedElement(this, RawLocator, index, innerType);
        }
        else
        {
            indexed = new ChildDocumentAtIndex(_options, this, _options.Serializer().Casing, index, innerType);
        }

        _members = _members.AddOrUpdate(key, indexed);
        return indexed;
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
