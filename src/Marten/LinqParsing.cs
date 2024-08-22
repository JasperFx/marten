using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using Marten.Events.Archiving;
using Marten.Linq.CreatedAt;
using Marten.Linq.Fields;
using Marten.Linq.LastModified;
using Marten.Linq.MatchesSql;
using Marten.Linq.Parsing;
using Marten.Linq.Parsing.Methods;
using Marten.Linq.SoftDeletes;
using Weasel.Postgresql.SqlGeneration;

namespace Marten;

public interface IReadOnlyLinqParsing
{
    /// <summary>
    ///     Registered extensions to the Marten Linq support for special handling of
    ///     specific .Net types
    /// </summary>
    public IReadOnlyList<IFieldSource> FieldSources { get; }

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
        new EnumerableContains(),
        new StringEndsWith(),
        new StringStartsWith(),
        new StringEquals(),
        new SimpleEqualsParser(),

        // Added
        new IsOneOf(),
        new EqualsIgnoreCaseParser(),
        new IsInGenericEnumerable(),
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
        new DictionaryExpressions(),

        // full text search
        new Search(),
        new PhraseSearch(),
        new PlainTextSearch(),
        new WebStyleSearch(),
        new NgramSearch()
    };


    /// <summary>
    ///     Add custom Linq expression parsers for your own methods
    /// </summary>
    public readonly IList<IMethodCallParser> MethodCallParsers = new List<IMethodCallParser>();

    private ImHashMap<Module, ImHashMap<int, IMethodCallParser>> _methodParsersByModule =
        ImHashMap<Module, ImHashMap<int, IMethodCallParser>>.Empty;

    internal LinqParsing()
    {
    }

    /// <summary>
    ///     Register extensions to the Marten Linq support for special handling of
    ///     specific .Net types
    /// </summary>
    public IList<IFieldSource> FieldSources { get; } = new List<IFieldSource>();

    IReadOnlyList<IFieldSource> IReadOnlyLinqParsing.FieldSources => FieldSources.ToList();

    IReadOnlyList<IMethodCallParser> IReadOnlyLinqParsing.MethodCallParsers => _parsers.ToList();


    internal ISqlFragment BuildWhereFragment(IFieldMapping mapping, MethodCallExpression expression,
        IReadOnlyStoreOptions options)
    {
        var parser = FindMethodParser(expression);

        if (parser == null)
        {
            throw new NotSupportedException(
                $"Marten does not (yet) support Linq queries using the {expression.Method.DeclaringType.FullName}.{expression.Method.Name}() method");
        }

        return parser.Parse(mapping, options, expression);
    }

    internal IMethodCallParser FindMethodParser(MethodCallExpression expression)
    {
        var module = expression.Method.Module;

        if (!_methodParsersByModule.TryFind(module, out var methodParsers))
        {
            methodParsers = ImHashMap<int, IMethodCallParser>.Empty;
            _methodParsersByModule = _methodParsersByModule.AddOrUpdate(module, methodParsers);
        }

        if (methodParsers.TryFind(expression.Method.MetadataToken, out var parser))
        {
            return parser;
        }

        parser = determineMethodParser(expression);

        _methodParsersByModule = _methodParsersByModule.AddOrUpdate(module, methodParsers.AddOrUpdate(expression.Method.MetadataToken, parser));

        return parser;
    }

    private IMethodCallParser determineMethodParser(MethodCallExpression expression)
    {
        return MethodCallParsers.FirstOrDefault(x => x.Matches(expression))
               ?? _parsers.FirstOrDefault(x => x.Matches(expression));
    }
}
