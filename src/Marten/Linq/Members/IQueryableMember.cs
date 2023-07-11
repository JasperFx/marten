using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Marten.Linq.Parsing.Operators;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public interface IQueryableMember: ISqlFragment
{
    Type MemberType { get; }

    /// <summary>
    ///     JSONPath segment name
    /// </summary>
    string JsonPathSegment { get; }

    string MemberName { get; }

    /// <summary>
    ///     Postgresql locator that also casts the raw string data to the proper Postgresql type
    /// </summary>
    string TypedLocator { get; }

    /// <summary>
    ///     Postgresql locator that returns the raw string value within the JSONB document
    /// </summary>
    string RawLocator { get; }

    /// <summary>
    ///     Locate the data for this field as JSONB
    /// </summary>
    string JSONBLocator { get; }

    IQueryableMember[] Ancestors { get; }

    /// <summary>
    ///     Locator for usage in "include" operations to get at the potential identifiers
    ///     of the included documents
    /// </summary>
    string LocatorForIncludedDocumentId { get; }

    /// <summary>
    ///     Locator for usage in is null / is not null checks
    /// </summary>
    string NullTestLocator { get; }

    /// <summary>
    ///     Build the locator or expression for usage within "ORDER BY" clauses
    /// </summary>
    /// <param name="ordering"></param>
    /// <param name="casingRule"></param>
    /// <returns></returns>
    string BuildOrderingExpression(Ordering ordering, CasingRule casingRule);

    /// <summary>
    ///     This facilitates containment operator filters as an ancestor member
    /// </summary>
    /// <param name="dict"></param>
    /// <returns></returns>
    Dictionary<string, object> FindOrPlaceChildDictionaryForContainment(Dictionary<string, object> dict);

    /// <summary>
    ///     This facilitates containment operator filters as the leaf member
    /// </summary>
    /// <param name="dict"></param>
    /// <param name="constant"></param>
    void PlaceValueInDictionaryForContainment(Dictionary<string, object> dict, ConstantExpression constant);

    string SelectorForDuplication(string pgType);
}
