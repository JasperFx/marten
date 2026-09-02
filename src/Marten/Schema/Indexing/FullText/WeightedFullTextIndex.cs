#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Parsing;
using Marten.Util;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Schema.Indexing.FullText;

/// <summary>
///     PostgreSQL's four text-search weight labels, most significant first.
/// </summary>
/// <remarks>
///     The labels themselves carry no numbers. A rank function supplies the values, defaulting to
///     <c>{D, C, B, A} = {0.1, 0.2, 0.4, 1.0}</c>, so <see cref="A" /> outranks <see cref="D" /> only
///     because that is the default array — a query may weight them differently. <see cref="D" /> is what
///     an unlabelled vector already means, which is why it is the default here.
/// </remarks>
public enum TextSearchWeight
{
    A,
    B,
    C,
    D
}

/// <summary>
///     Assigns a <see cref="TextSearchWeight" /> per member so that a match in one field can outrank a
///     match in another.
/// </summary>
/// <remarks>
///     <para>
///         #5298. Marten's ordinary full-text index concatenates its members as TEXT and converts once,
///         which produces a single flat vector in which every match is equally relevant:
///     </para>
///     <code>
///     to_tsvector('english', ((data ->> 'Title') || ' ' || (data ->> 'Description')))
///     </code>
///     <para>
///         Weighting cannot be expressed that way. <c>setweight</c> labels a <em>vector</em>, so each
///         member is converted separately and the VECTORS are concatenated:
///     </para>
///     <code>
///     setweight(to_tsvector('english', coalesce(data ->> 'Title', '')), 'A') ||
///     setweight(to_tsvector('english', coalesce(data ->> 'Description', '')), 'C')
///     </code>
///     <para>
///         <c>coalesce</c> is not decoration. <c>to_tsvector</c> of NULL is NULL, and concatenating NULL
///         into a vector annihilates the whole expression — so one absent member would silently empty the
///         index row for that document.
///     </para>
/// </remarks>
public class WeightedFullTextIndexExpression<T>
{
    private readonly List<(MemberInfo[] Members, TextSearchWeight Weight)> _members = new();

    /// <summary>
    ///     Include a member in the index at the given weight.
    /// </summary>
    /// <param name="expression">The member to index</param>
    /// <param name="weight">
    ///     Its weight label. Defaults to <see cref="TextSearchWeight.D" />, which is what an unlabelled
    ///     vector already means.
    /// </param>
    public WeightedFullTextIndexExpression<T> Weighted(
        Expression<Func<T, object?>> expression,
        TextSearchWeight weight = TextSearchWeight.D)
    {
        var members = FindMembers.Determine(expression);
        if (members.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expression),
                $"Unable to determine a member from {expression}.");
        }

        _members.Add((members, weight));
        return this;
    }

    internal MemberInfo[][] Members => _members.Select(x => x.Members).ToArray();

    internal bool IsEmpty => _members.Count == 0;

    /// <summary>
    ///     True when every member carries the same label, which makes the weighting a no-op.
    /// </summary>
    /// <remarks>
    ///     Rejected at configuration time rather than silently emitting a pointless <c>setweight</c>.
    ///     Relative ranking is the entire point: one weight, or the same weight everywhere, ranks nothing,
    ///     and an index that looks weighted but is not is worse than an error — it invites a caller to
    ///     build a ranked screen over it and ship it.
    /// </remarks>
    internal bool IsUniform => _members.Select(x => x.Weight).Distinct().Count() <= 1;

    /// <summary>
    ///     Build the <c>tsvector</c> expression this index is indexed over. The result is a vector at the
    ///     top level, which is why it goes to <see cref="FullTextIndexDefinition.ForTsVector" /> rather
    ///     than to <c>DocumentConfig</c>.
    /// </summary>
    internal string BuildTsVectorExpression(DocumentMapping mapping, string regConfig)
    {
        return _members
            .Select(m =>
            {
                var locator = mapping.QueryMembers.MemberFor(m.Members).RawLocator.RemoveTableAlias("d");
                return $"setweight(to_tsvector('{regConfig}', coalesce({locator}, '')), '{m.Weight}')";
            })
            .Join(" || ");
    }
}
