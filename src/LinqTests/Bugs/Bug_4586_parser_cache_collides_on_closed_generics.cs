using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace LinqTests.Bugs;

// #4586: LinqParsing.FindMethodParser keyed its cache by
// (Module, MetadataToken). For generic methods the MetadataToken is the
// same regardless of the closed-over T (Enumerable.Contains<StrongId> and
// Enumerable.Contains<string> share a token), so the first parser to match
// any T was returned for every subsequent T — even when the new T didn't
// match the parser's Matches() guard.
//
// Verifies the cache now keys by the closed MethodInfo, so a custom parser
// gated on T == Bug4586StrongId only intercepts that closed generic and
// the same Enumerable.Contains call with T == string falls through to the
// standard handling.
public class Bug_4586_parser_cache_collides_on_closed_generics
{
    [Fact]
    public async Task custom_contains_parser_for_one_generic_argument_does_not_intercept_another()
    {
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug_4586";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Schema.For<Bug4586Doc>();
            opts.RegisterValueType<Bug4586StrongId>();
            // Insert at position 0 so the custom parser wins ahead of the
            // built-in Enumerable.Contains handling for the T it actually
            // matches — but not for any other T.
            opts.Linq.MethodCallParsers.Insert(0, new Bug4586StrongIdContainsParser());
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var strongIds = new[] { new Bug4586StrongId("strong-1") };
        var names = new[] { "alpha" };

        // First call: should hit the custom parser, which throws.
        Bug4586StrongIdContainsParser.HitCount = 0;
        await using (var session = store.QuerySession())
        {
            await Should.ThrowAsync<InvalidOperationException>(async () =>
            {
                _ = await session.Query<Bug4586Doc>()
                    .Where(d => Enumerable.Contains(strongIds, d.ExternalId))
                    .ToListAsync();
            });
        }
        Bug4586StrongIdContainsParser.HitCount.ShouldBe(1);

        // Second call: SAME generic method, different closed T. Must NOT
        // get the cached StrongId-only parser. Should round-trip normally.
        await using (var session = store.QuerySession())
        {
            var results = await session.Query<Bug4586Doc>()
                .Where(d => Enumerable.Contains(names, d.Name))
                .ToListAsync();
            results.ShouldBeEmpty();
        }
        // HitCount didn't move — the custom parser wasn't even consulted.
        Bug4586StrongIdContainsParser.HitCount.ShouldBe(1);
    }
}

public readonly record struct Bug4586StrongId(string Value);

public sealed class Bug4586Doc
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Bug4586StrongId ExternalId { get; init; }
}

internal sealed class Bug4586StrongIdContainsParser: IMethodCallParser
{
    // Test-only side channel — confirms whether Matches/Parse ran. A
    // ConcurrentDictionary keyed by closed MethodInfo would be cleaner,
    // but the test fixture is single-threaded.
    public static int HitCount;

    private static readonly MethodInfo EnumerableContainsMethod =
        typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.IsGenericMethod
               && expression.Method.GetGenericMethodDefinition() == EnumerableContainsMethod
               && expression.Method.GetGenericArguments()[0] == typeof(Bug4586StrongId);
    }

    public ISqlFragment Parse(
        IQueryableMemberCollection memberCollection,
        IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        HitCount++;
        // Match the issue's repro shape — first call throws so the test
        // catches it; the second call must never reach this method.
        throw new InvalidOperationException("Bug4586StrongIdContainsParser was invoked.");
    }
}
