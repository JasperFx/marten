using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
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
using Newtonsoft.Json.Linq;

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
        new SimpleEqualsParser(),
        new AnySubQueryParser(),

        // Keep this below the string methods!
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

        // last modified
        new ModifiedSinceParser(),
        new ModifiedBeforeParser(),

        // last modified
        new CreatedSinceParser(),
        new CreatedBeforeParser(),

        // matches sql
        new MatchesSqlParser(),

        // dictionaries
        new DictionaryContainsKey(),

        // full text search
        new Search(),
        new PhraseSearch(),
        new PlainTextSearch(),
        new WebStyleSearch(),
        new NgramSearch()
    };

    private readonly StoreOptions _options;

    private static readonly HashSet<string> Encoutered = new HashSet<string>();

    /// <summary>
    ///     Add custom Linq expression parsers for your own methods
    /// </summary>
    public readonly IList<IMethodCallParser> MethodCallParsers = new List<IMethodCallParser>();

    private ImHashMap<string, IMethodCallParser> _methodParsers = ImHashMap<string, IMethodCallParser>.Empty;

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

    IReadOnlyList<IMethodCallParser> IReadOnlyLinqParsing.MethodCallParsers => _parsers.ToList();


    internal IMethodCallParser FindMethodParser(MethodCallExpression expression)
    {
        var key = ToKey(expression.Method);

        if (_methodParsers.TryFind(key, out var parser))
        {
            return parser;
        }

        parser = determineMethodParser(expression);
        _methodParsers = _methodParsers.AddOrUpdate(key, parser);
        return parser;
    }

    private IMethodCallParser determineMethodParser(MethodCallExpression expression)
    {
        return MethodCallParsers.FirstOrDefault(x => x.Matches(expression))
               ?? _parsers.FirstOrDefault(x => x.Matches(expression));
    }

    /// <summary>
    ///     https://learn.microsoft.com/en-us/dotnet/api/system.reflection.memberinfo.metadatatoken?view=net-8.0
    ///     MetadataToken -- "A value which, in combination with Module, uniquely identifies a metadata element."
    /// </summary>
    private static string ToKey(MethodInfo expressionMethod)
    {
        Encoutered.Add(
            $"{expressionMethod.Module.Name}_{expressionMethod.Module.MetadataToken}_{expressionMethod.MetadataToken}_{expressionMethod.DeclaringType?.Name}_{expressionMethod.Name}");

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("---");
        Console.WriteLine();
        foreach (var x in Encoutered.ToArray())
        {
            Console.WriteLine(x);
        }

        return $"{expressionMethod.Module.Name}_{expressionMethod.MetadataToken}";
    }
}
