using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ImTools;
using JasperFx.Core;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Linq.CreatedAt;
using Marten.Linq.LastModified;
using Marten.Linq.MatchesSql;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Methods;
using Marten.Linq.Parsing.Methods.FullText;
using Marten.Linq.Parsing.Methods.Strings;
using Marten.Linq.SoftDeletes;

namespace Marten;

public interface IReadOnlyLinqParsing
{
    /// <summary>
    ///     Registered extensions to the Marten Linq support for special handling of
    ///     specific .Net types
    /// </summary>
    public IReadOnlyList<IMemberSource> FieldSources { get; }

    /// <summary>
    ///     Custom Linq expression parsers for your own methods
    /// </summary>
    public IReadOnlyList<IMethodCallParser> MethodCallParsers { get; }
}

public class LinqParsing: IReadOnlyLinqParsing
{
    // The out of the box method call parsers
    private static readonly IList<IMethodCallParser> _parsers = new List<IMethodCallParser>
    {
        new StringContains(),
        new StringEndsWith(),
        new StringStartsWith(),
        new StringIsNullOrEmpty(),
        new StringIsNullOrWhiteSpace(),
        new StringEquals(),
        new RegexIsMatch(),
        new SimpleEqualsParser(),
        new AnySubQueryParser(),

        // Keep this below the string methods!
        new MemoryExtensionsContains(),
        new EnumerableContains(),
        new HashSetEnumerableContains(),
        new AllMethodParser(),

        // Added
        new IsOneOf(),
        new EqualsIgnoreCaseParser(),
        new IsEmpty(),
        new IsSupersetOf(),
        new IsSubsetOf(),

        // multi-tenancy
        new AnyTenant(),
        new TenantIsOneOf(),

        // soft deletes
        new MaybeDeletedParser(),
        new IsDeletedParser(),
        new DeletedSinceParser(),
        new DeletedBeforeParser(),

        // event is archived
        new MaybeArchivedMethodCallParser(),
        new EventTypesAreParser(),
        new HasTagParser(),

        // last modified
        new ModifiedSinceParser(),
        new ModifiedBeforeParser(),

        // last modified
        new CreatedSinceParser(),
        new CreatedBeforeParser(),

        // matches sql
        new MatchesSqlParser(),
        new MatchesJsonPathParser(),

        // dictionaries
        new DictionaryContainsKey(),

        // full text search
        new Search(),
        new PhraseSearch(),
        new PlainTextSearch(),
        new WebStyleSearch(),
        new NgramSearch(),
        new PrefixSearch()
    };

    private readonly StoreOptions _options;


    /// <summary>
    ///     Add custom Linq expression parsers for your own methods
    /// </summary>
    public readonly IList<IMethodCallParser> MethodCallParsers = new List<IMethodCallParser>();

    // #4586: key by MethodInfo, not (Module, MetadataToken). The MetadataToken
    // is the same for every closed generic of a given generic method definition
    // (e.g. Enumerable.Contains<StrongId> and Enumerable.Contains<string> share
    // a token), so the old cache returned whichever parser landed first for any
    // subsequent T. MethodInfo.Equals / GetHashCode on the closed reified
    // MethodInfo correctly distinguishes the two.
    private ImHashMap<MethodInfo, IMethodCallParser> _methodParsers =
        ImHashMap<MethodInfo, IMethodCallParser>.Empty;

    internal LinqParsing(StoreOptions options)
    {
        _options = options;
    }

    /// <summary>
    ///     Register extensions to the Marten Linq support for special handling of
    ///     specific .Net types
    /// </summary>
    public IList<IMemberSource> MemberSources { get; } = new List<IMemberSource>();

    IReadOnlyList<IMemberSource> IReadOnlyLinqParsing.FieldSources => MemberSources.ToList();

    private readonly List<IMemberSource> _defaultSources = [new ValueTypeMemberSource()];

    internal IEnumerable<IMemberSource> allMemberSources()
    {
        foreach (var memberSource in MemberSources)
        {
            yield return memberSource;
        }

        foreach (var defaultSource in _defaultSources)
        {
            yield return defaultSource;
        }
    }

    IReadOnlyList<IMethodCallParser> IReadOnlyLinqParsing.MethodCallParsers => _parsers.ToList();

    internal IMethodCallParser FindMethodParser(MethodCallExpression expression)
    {
        // #4586: key by the closed MethodInfo. For generic methods like
        // Enumerable.Contains<T> the MetadataToken-keyed cache used previously
        // collapsed every closed generic (StrongId, string, …) into a single
        // slot — the first parser that matched any T was returned for all
        // subsequent Ts. MethodInfo's overridden Equals / GetHashCode include
        // the closed generic arguments, so each closed reified method gets its
        // own cache entry.
        if (_methodParsers.TryFind(expression.Method, out var cached))
        {
            return cached;
        }

        var parser = determineMethodParser(expression);
        _methodParsers = _methodParsers.AddOrUpdate(expression.Method, parser);
        return parser;
    }

    private IMethodCallParser determineMethodParser(MethodCallExpression expression)
    {
        return MethodCallParsers.FirstOrDefault(x => x.Matches(expression))
               ?? _parsers.FirstOrDefault(x => x.Matches(expression));
    }
}
