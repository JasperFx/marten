using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace LinqTests.Bugs;

/// <summary>
/// Execution-time bug — distinct from the parse-time #4599 / #4600 / #4601 family.
///
/// <para>
/// When a document member is a CLR <c>enum</c> and the store is configured with
/// <see cref="EnumStorage.AsString"/>, an <c>in</c> predicate of the shape
/// <c>enumValues.Contains(doc.EnumMember)</c> (the form HotChocolate's
/// <c>[UseFiltering]</c> <c>in</c> operator emits) parses correctly but fails at
/// execution with an Npgsql <see cref="InvalidOperationException"/>:
/// </para>
/// <code>
/// Writing values of 'PurchaseStatus[]' is not supported for parameters
/// having NpgsqlDbType '-2147483639'.
/// </code>
/// <para>
/// The scalar-eq path on the same member works (the <c>EnumAsStringMember</c>
/// converts the constant to its name via <c>Enum.GetName</c>). The
/// <c>EnumerableContains</c> path does not — it builds a <c>CommandParameter</c>
/// from the raw enum array, and Npgsql has no mapping for <c>EnumType[]</c>
/// when the enum isn't a registered Postgres enum type.
/// </para>
/// </summary>
public class Bug_enum_asstring_array_contains: BugIntegrationContext
{

    public enum PurchaseStatus { Unpaid, Paid, Voided }

    public sealed class Purchase
    {
        public Guid Id { get; set; }
        public PurchaseStatus Status { get; set; }
    }

    private void BuildAsStringStore()
    {
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(enumStorage: EnumStorage.AsString);
            opts.Schema.For<Purchase>();
        });
    }

    private async Task seedAsync()
    {
        await using var session = theStore.LightweightSession();
        session.Store(
            new Purchase { Id = Guid.NewGuid(), Status = PurchaseStatus.Unpaid },
            new Purchase { Id = Guid.NewGuid(), Status = PurchaseStatus.Paid },
            new Purchase { Id = Guid.NewGuid(), Status = PurchaseStatus.Voided });
        await session.SaveChangesAsync();
    }

    // ---- canary: scalar eq has always worked under AsString ----

    [Fact]
    public async Task scalar_eq_on_AsString_enum_member_executes()
    {
        BuildAsStringStore();
        await seedAsync();

        await using var query = theStore.QuerySession();
        var unpaid = await query.Query<Purchase>()
            .Where(p => p.Status == PurchaseStatus.Unpaid)
            .ToListAsync();
        unpaid.Count.ShouldBe(1);
    }

    // ---- headline regression: in / Contains on AsString enum ----

    [Fact]
    public async Task contains_array_on_AsString_enum_member_executes()
    {
        BuildAsStringStore();
        await seedAsync();

        // The HotChocolate [UseFiltering] in-operator shape:
        //   where: { status: { in: [UNPAID, VOIDED] } }
        // translates to `values.Contains(p.Status)` against an enum CLR array.
        var values = new[] { PurchaseStatus.Unpaid, PurchaseStatus.Voided };

        await using var query = theStore.QuerySession();
        var rows = await query.Query<Purchase>()
            .Where(p => values.Contains(p.Status))
            .ToListAsync();

        rows.Count.ShouldBe(2);
        rows.Select(p => p.Status).OrderBy(s => s)
            .ShouldBe(new[] { PurchaseStatus.Unpaid, PurchaseStatus.Voided });
    }

    [Fact]
    public async Task contains_list_on_AsString_enum_member_executes()
    {
        // The List<> shape — HotChocolate sometimes hands a List rather than an array.
        BuildAsStringStore();
        await seedAsync();

        var values = new System.Collections.Generic.List<PurchaseStatus>
        {
            PurchaseStatus.Paid, PurchaseStatus.Voided
        };

        await using var query = theStore.QuerySession();
        var rows = await query.Query<Purchase>()
            .Where(p => values.Contains(p.Status))
            .ToListAsync();

        rows.Count.ShouldBe(2);
        rows.Select(p => p.Status).OrderBy(s => s)
            .ShouldBe(new[] { PurchaseStatus.Paid, PurchaseStatus.Voided });
    }

    [Fact]
    public async Task contains_single_element_on_AsString_enum_member_executes()
    {
        // Boundary: collection of one — the IN reduces to a single element.
        BuildAsStringStore();
        await seedAsync();

        var values = new[] { PurchaseStatus.Paid };

        await using var query = theStore.QuerySession();
        var rows = await query.Query<Purchase>()
            .Where(p => values.Contains(p.Status))
            .ToListAsync();

        rows.Count.ShouldBe(1);
        rows[0].Status.ShouldBe(PurchaseStatus.Paid);
    }

    [Fact]
    public async Task contains_empty_collection_on_AsString_enum_member_executes()
    {
        // Boundary: empty collection — IN of nothing matches no rows. Must not throw.
        BuildAsStringStore();
        await seedAsync();

        var values = Array.Empty<PurchaseStatus>();

        await using var query = theStore.QuerySession();
        var rows = await query.Query<Purchase>()
            .Where(p => values.Contains(p.Status))
            .ToListAsync();

        rows.Count.ShouldBe(0);
    }

    // ---- canary: AsInteger storage already worked on the Contains path ----

    [Fact]
    public async Task contains_array_on_AsInteger_enum_member_still_executes()
    {
        // Pin the AsInteger path so the fix to the AsString branch doesn't
        // accidentally break the existing integer-storage behavior.
        StoreOptions(opts =>
        {
            opts.UseSystemTextJsonForSerialization(enumStorage: EnumStorage.AsInteger);
            opts.Schema.For<Purchase>();
        });

        await using (var session = theStore.LightweightSession())
        {
            session.Store(
                new Purchase { Id = Guid.NewGuid(), Status = PurchaseStatus.Unpaid },
                new Purchase { Id = Guid.NewGuid(), Status = PurchaseStatus.Paid },
                new Purchase { Id = Guid.NewGuid(), Status = PurchaseStatus.Voided });
            await session.SaveChangesAsync();
        }

        var values = new[] { PurchaseStatus.Unpaid, PurchaseStatus.Voided };

        await using var query = theStore.QuerySession();
        var rows = await query.Query<Purchase>()
            .Where(p => values.Contains(p.Status))
            .ToListAsync();

        rows.Count.ShouldBe(2);
    }
}
