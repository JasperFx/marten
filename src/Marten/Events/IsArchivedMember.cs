using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using JasperFx.Events;
using Marten.Events.Archiving;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing.Operators;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events;

internal class IsArchivedMember: IQueryableMember, IComparableMember, IBooleanMember
{
    private static readonly string _locator = "d.is_archived";

    public void Apply(IPostgresqlCommandBuilder builder)
    {
        builder.Append(_locator);
    }

    public ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        var value = constant.Value.Equals(true);

        return value ? IsArchivedFilter.Instance : IsNotArchivedFilter.Instance;
    }

    public Type MemberType => typeof(bool);
    public string JsonPathSegment => string.Empty;
    public string MemberName => nameof(IEvent.IsArchived);
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
        return IsArchivedFilter.Instance;
    }
}
