using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

// Top-level types so the generated mt_doc_* table name stays under the 63-byte
// PostgreSQL identifier limit (the test class name itself is long).
//
// Note: the ToString() override is the trigger. ConstantExpression.ToString() wraps
// a value in "value(<TypeName>)" only when the value's ToString() is the default
// Object.ToString(); when the type overrides ToString(), the custom string is used
// directly. IsCompilableExpression's "value(" string-prefix check then returns false
// and the receiver is wrongly rejected. HotChocolate's ExpressionParameter<T> has a
// custom ToString() for diagnostics, which is why their `[UseFiltering]` `in`
// operator triggers this in practice.
public sealed class Bug4599ValueHolder<T>
{
    public IEnumerable<T> p { get; }
    public Bug4599ValueHolder(IEnumerable<T> values) => p = values;
    public override string ToString() => $"Bug4599ValueHolder[{string.Join(',', p)}]";
}

public sealed class Bug4599HashSetHolder<T>
{
    public HashSet<T> p { get; }
    public Bug4599HashSetHolder(HashSet<T> values) => p = values;
    public override string ToString() => $"Bug4599HashSetHolder[{string.Join(',', p)}]";
}

public class Bug4599Doc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

// https://github.com/JasperFx/marten/issues/4599
//
// EnumerableContains.Parse crashes with
//   "variable '<x>' of type '<Document>' referenced from scope '', but it is not defined"
// when the Contains() receiver is built programmatically as
// Property(Constant(<wrapper>), "<member>") and the wrapper overrides ToString().
// HotChocolate's [UseFiltering] `in` operator is one consumer that produces this
// shape via FilterExpressionBuilder.CreateAndConvertParameter.
//
// Each test uses its own DocumentStore with a unique schema so that parallel
// multi-target test runs (net8.0 / net9.0 / net10.0) don't interfere through a
// shared mt_doc_bug4599doc table.
public class Bug_4599_enumerable_contains_programmatic_receiver
{
    private static IDocumentStore BuildStore(string schema)
    {
        // Include the runtime major version so parallel multi-target test runs
        // (net8.0 / net9.0 / net10.0) don't collide on the same schema in the
        // shared marten_testing DB.
        var suffix = Environment.Version.Major;
        return DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"{schema}{suffix}";
        });
    }

    private static async Task storeThreeDocs(IDocumentStore store)
    {
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await using var session = store.LightweightSession();
        session.Store(new Bug4599Doc { Id = Guid.NewGuid(), Name = "a" });
        session.Store(new Bug4599Doc { Id = Guid.NewGuid(), Name = "b" });
        session.Store(new Bug4599Doc { Id = Guid.NewGuid(), Name = "c" });
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task enumerable_contains_with_programmatic_receiver_should_query_correctly()
    {
        await using var store = BuildStore("b4599e");
        await storeThreeDocs(store);

        var holder = new Bug4599ValueHolder<string>(new[] { "a", "b" });

        // Build: doc => holder.p.Contains(doc.Name)
        // -- the receiver is Property(Constant(holder), "p"), NOT a compiler-emitted closure.
        var doc = Expression.Parameter(typeof(Bug4599Doc), "doc");
        var nameAccess = Expression.Property(doc, nameof(Bug4599Doc.Name));
        var receiver = Expression.Property(Expression.Constant(holder), nameof(Bug4599ValueHolder<string>.p));
        var containsCall = Expression.Call(
            typeof(Enumerable),
            nameof(Enumerable.Contains),
            [typeof(string)],
            receiver,
            nameAccess);
        var predicate = Expression.Lambda<Func<Bug4599Doc, bool>>(containsCall, doc);

        await using var query = store.QuerySession();
        var results = await query.Query<Bug4599Doc>().Where(predicate).ToListAsync();

        results.Select(x => x.Name).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("a", "b");
    }

    [Fact]
    public async Task hashset_contains_with_programmatic_receiver_should_query_correctly()
    {
        await using var store = BuildStore("b4599h");
        await storeThreeDocs(store);

        var holder = new Bug4599HashSetHolder<string>(new HashSet<string> { "a", "b" });

        // Build: doc => holder.p.Contains(doc.Name) using HashSet<T>.Contains (instance method)
        var doc = Expression.Parameter(typeof(Bug4599Doc), "doc");
        var nameAccess = Expression.Property(doc, nameof(Bug4599Doc.Name));
        var receiver = Expression.Property(Expression.Constant(holder), nameof(Bug4599HashSetHolder<string>.p));
        var containsMethod = typeof(HashSet<string>).GetMethod(nameof(HashSet<string>.Contains), [typeof(string)])!;
        var containsCall = Expression.Call(receiver, containsMethod, nameAccess);
        var predicate = Expression.Lambda<Func<Bug4599Doc, bool>>(containsCall, doc);

        await using var query = store.QuerySession();
        var results = await query.Query<Bug4599Doc>().Where(predicate).ToListAsync();

        results.Select(x => x.Name).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("a", "b");
    }
}
