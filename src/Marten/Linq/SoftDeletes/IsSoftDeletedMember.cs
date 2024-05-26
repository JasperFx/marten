#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Events.Archiving;
using Marten.Linq.Members;
using Marten.Linq.Parsing.Operators;
using Marten.Schema;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SoftDeletes;

public class IsSoftDeletedMember : IQueryableMember, IComparableMember, IBooleanMember
{
    private static readonly string _locator = $"d.{SchemaConstants.DeletedColumn}";

    public IsSoftDeletedMember(MemberInfo member)
    {
        MemberName = member.Name;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append(_locator);
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var value = constant.Value!.Equals(true);

        return value ? IsDeletedFilter.Instance : IsNotDeletedFilter.Instance;
    }

    public Type MemberType => typeof(bool);
    public string JsonPathSegment => string.Empty;
    public string MemberName { get; }
    public string TypedLocator  => _locator;
    public string RawLocator  => _locator;
    public string JSONBLocator  => _locator;
    public IQueryableMember[] Ancestors => Array.Empty<IQueryableMember>();
    public string LocatorForIncludedDocumentId  => _locator;
    public string NullTestLocator => _locator;
    public string BuildOrderingExpression(Ordering ordering, CasingRule casingRule)
    {
        return ordering.Direction == OrderingDirection.Asc ? _locator : _locator + " desc";
    }

    public Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict)
    {
        throw new NotSupportedException();
    }

    public void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant)
    {
        throw new NotSupportedException();
    }

    public string SelectorForDuplication(string pgType)
    {
        throw new NotSupportedException();
    }

    public ISqlFragment BuildIsTrueFragment()
    {
        return IsDeletedFilter.Instance;
    }
}
