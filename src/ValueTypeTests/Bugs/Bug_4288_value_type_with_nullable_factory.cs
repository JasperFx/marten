using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Vogen;

namespace ValueTypeTests.Bugs;

public class Bug_4288_value_type_with_nullable_factory : BugIntegrationContext
{
    [Fact]
    public void can_generate_ddl_for_index_over_value_type_with_nullable_sibling_factory()
    {
        // Adding a `static FromNullable(string?)` sibling to a Vogen value object used to make
        // Marten select that method as the value type's builder, which then crashed in
        // ValueTypeInfo.CreateWrapper because the return type is Nullable<Bug4288Value>, not
        // Bug4288Value. The crash surfaced when generating DDL for any computed index over
        // the value type.
        StoreOptions(opts =>
        {
            opts.RegisterValueType<Bug4288Value>();
            opts.Schema.For<Bug4288Doc>().Index(x => x.Value);
        });

        Should.NotThrow(() => theStore.Storage.ToDatabaseScript());
    }

    [Fact]
    public async Task can_round_trip_and_query_value_type_with_nullable_sibling_factory()
    {
        StoreOptions(opts =>
        {
            opts.RegisterValueType<Bug4288Value>();
        });

        var doc = new Bug4288Doc { Value = Bug4288Value.From("abc") };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        var key = Bug4288Value.From("abc");
        var found = await theSession.Query<Bug4288Doc>()
            .Where(x => x.Value == key)
            .ToListAsync();

        found.Count.ShouldBe(1);
        found.Single().Id.ShouldBe(doc.Id);
    }
}

[ValueObject<string>]
public readonly partial struct Bug4288Value
{
    private static Validation Validate(string value)
        => string.IsNullOrWhiteSpace(value) ? Validation.Invalid("Cannot be empty.") : Validation.Ok;

    public static Bug4288Value? FromNullable(string? value)
        => value is null ? null : From(value);
}

public class Bug4288Doc
{
    public Guid Id { get; set; }
    public Bug4288Value? Value { get; set; }
}
