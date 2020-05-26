using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Linq.LastModified;
using Marten.Linq.MatchesSql;
using Marten.Linq.Parsing;
using Marten.Linq.SoftDeletes;
using Marten.Schema;
using Marten.Util;


namespace Marten
{
    public delegate IWhereFragment MethodCallParseDelegate(MethodCallExpression expression, IQueryableDocument mapping);

    public class LinqParsing
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

            // last modified
            new ModifiedSinceParser(),
            new ModifiedBeforeParser(),

            // matches sql
            new MatchesSqlParser(),

            // dictionaries
            new DictionaryExpressions(),

            // full text search
            new Search(),
            new PhraseSearch(),
            new PlainTextSearch(),
            new WebStyleSearch()
        };


        public LinqParsing()
        {
        }

        /// <summary>
        ///     Add custom Linq expression parsers for your own methods
        /// </summary>
        public readonly IList<IMethodCallParser> MethodCallParsers = new List<IMethodCallParser>();



        internal IWhereFragment BuildWhereFragment(IQueryableDocument mapping, MethodCallExpression expression, ISerializer serializer)
        {
            var parser = FindMethodParser(expression);

            if (parser == null)
            {
                throw new NotSupportedException(
                    $"Marten does not (yet) support Linq queries using the {expression.Method.DeclaringType.FullName}.{expression.Method.Name}() method");
            }

            return parser.Parse(mapping, serializer, expression);
        }

        internal IMethodCallParser FindMethodParser(MethodCallExpression expression)
        {

            if (_methodParsing.TryFind(expression.Method.DeclaringType, out var byName))
            {
                if (byName.TryFind(expression.Method.Name, out var p))
                {
                    return p;
                }
            }

            byName = byName ?? ImHashMap<string, IMethodCallParser>.Empty;
            var parser = determineMethodParser(expression);
            byName = byName.AddOrUpdate(expression.Method.Name, parser);
            _methodParsing = _methodParsing.AddOrUpdate(expression.Method.DeclaringType, byName);

            return parser;
        }

        private IMethodCallParser determineMethodParser(MethodCallExpression expression)
        {
            return MethodCallParsers.FirstOrDefault(x => x.Matches(expression))
                   ?? _parsers.FirstOrDefault(x => x.Matches(expression));
        }

        private ImHashMap<Type, ImHashMap<string, IMethodCallParser>> _methodParsing = ImHashMap<Type, ImHashMap<string, IMethodCallParser>>.Empty;


    }
}
