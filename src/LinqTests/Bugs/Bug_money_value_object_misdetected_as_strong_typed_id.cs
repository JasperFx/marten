using System;
using System.Linq;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace LinqTests.Bugs;

// Multi-property record structs (e.g. Money(decimal Amount, Guid CurrencyId)) used as
// document properties were being mis-classified as strong-typed-id wrappers by
// ValueTypeIdGeneration.IsCandidate. The candidate filter only counted properties whose
// type is in DocumentMapping.ValidIdTypes (Guid/int/long/string), so it saw only
// CurrencyId, matched a static `Money Zero(Guid)` builder, and as a side effect called
// PostgresqlProvider.Instance.RegisterMapping(typeof(Money), "uuid", ...) — globally
// poisoning Weasel's type map. LINQ resolution then treated Money as a scalar
// (SimpleCastMember) instead of a JSONB document (ChildDocument), so any nested member
// access like `x.UnappliedAmount.Amount` threw BadLinqExpressionException.
//
// Each test uses its own unique record-struct type to avoid cross-test pollution of
// the process-wide PostgresqlProvider.Instance singleton.
public class Bug_money_value_object_misdetected_as_strong_typed_id
{
    [Fact]
    public void multi_property_record_struct_does_not_pollute_global_pg_type_mapping()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Schema.For<MoneyDocA>();
        });

        var pgType = PostgresqlProvider.Instance.GetDatabaseType(
            typeof(MoneyA), store.Options.Serializer().EnumStorage);

        pgType.ShouldBe("jsonb");
    }

    [Fact]
    public void linq_can_resolve_nested_member_access_on_multi_property_record_struct()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.Schema.For<MoneyDocB>();
        });

        using var session = store.QuerySession();
        var queryable = session.Query<MoneyDocB>()
            .Where(x => x.Amount.Value > 0m && x.Amount.CurrencyId == Guid.Empty);

        Should.NotThrow(() => queryable.ToCommand());
    }
}

public readonly record struct MoneyA(decimal Value, Guid CurrencyId)
{
    public static MoneyA Zero(Guid currencyId) => new(0m, currencyId);
}

public class MoneyDocA
{
    public Guid Id { get; set; }
    public MoneyA Amount { get; set; }
}

public readonly record struct MoneyB(decimal Value, Guid CurrencyId)
{
    public static MoneyB Zero(Guid currencyId) => new(0m, currencyId);
}

public class MoneyDocB
{
    public Guid Id { get; set; }
    public MoneyB Amount { get; set; }
}
