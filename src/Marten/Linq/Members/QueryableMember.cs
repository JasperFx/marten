using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Operators;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public abstract class QueryableMember: IQueryableMember, IHasChildrenMembers
{
    private HasValueMember? _hasValue;
    private string _nullTestLocator;
    private string _descendingOrderLocator;

    /// <summary>
    /// </summary>
    /// <param name="parentLocator">JSONB location of the parent element</param>
    /// <param name="member"></param>
    protected QueryableMember(IQueryableMember parent, Casing casing, MemberInfo member)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));

        Member = member;
        MemberType = member.GetMemberType();

        JsonPathSegment = MemberName = member.ToJsonKey(casing);
        RawLocator = TypedLocator = $"{parent.RawLocator} ->> '{MemberName}'";

        JSONBLocator = $"CAST({RawLocator} as jsonb)";
        Ancestors = parent.Ancestors.Append(parent);
        _descendingOrderLocator = $"{TypedLocator} desc";
    }

    protected QueryableMember(IQueryableMember parent, string memberName, Type memberType)
    {
        MemberName = memberName;
        MemberType = memberType;

        RawLocator = TypedLocator = $"{parent.RawLocator} ->> '{MemberName}'";

        JSONBLocator = $"CAST({RawLocator} as jsonb)";

        Ancestors = parent.Ancestors.Append(parent);
        _descendingOrderLocator = $"{TypedLocator} desc";
    }

    public MemberInfo Member { get; }

    public virtual IQueryableMember FindMember(MemberInfo member)
    {
        if (member.Name == "Value")
        {
            return this;
        }

        if (member.Name == "HasValue")
        {
            _hasValue ??= new HasValueMember(this);
            return _hasValue;
        }

        throw new BadLinqExpressionException(
            $"Marten does not (yet) support member {MemberType.ShortNameInCode()}.{member.Name}");
    }

    public virtual void ReplaceMember(MemberInfo member, IQueryableMember queryableMember)
    {
        throw new NotSupportedException();
    }

    public string JsonPathSegment { get; protected set; }

    public string MemberName { get; }

    public IQueryableMember[] Ancestors { get; }

    public string RawLocator { get; protected internal set; }


    public string NullTestLocator
    {
        get => _nullTestLocator ?? RawLocator;
        protected set => _nullTestLocator = value;
    }

    public string TypedLocator { get; protected internal set; }

    /// <summary>
    ///     Locate the data for this field as JSONB
    /// </summary>
    public string JSONBLocator { get; set; }

    public Type MemberType { get; }

    public virtual string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        return ordering.Direction == OrderingDirection.Desc ? _descendingOrderLocator : TypedLocator;
    }

    void ISqlFragment.Apply(CommandBuilder builder)
    {
        builder.Append(TypedLocator);
    }

    bool ISqlFragment.Contains(string sqlText)
    {
        return false;
    }

    public virtual Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
    {
        if (dict.TryGetValue(MemberName, out var child))
        {
            if (child is Dictionary<string, object> c)
            {
                return c;
            }
        }

        var newDict = new Dictionary<string, object>();
        dict[MemberName] = newDict;

        return newDict;
    }

    public virtual void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict,
        ConstantExpression constant)
    {
        dict[MemberName] = constant.Value();
    }

    public virtual string LocatorForIncludedDocumentId => TypedLocator;


    public virtual string SelectorForDuplication(string pgType)
    {
        return $"CAST({RawLocator.Replace("d.", "")} as {pgType})";
    }

    public virtual ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        if (constant.Value == null)
        {
            return op == "=" ? new IsNullFilter(this) : new IsNotNullFilter(this);
        }

        var def = new CommandParameter(constant);
        return new MemberComparisonFilter(this, def, op);
    }
}
