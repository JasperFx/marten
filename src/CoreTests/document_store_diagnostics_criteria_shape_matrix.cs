#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using JasperFx.Linq;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public enum CriteriaOrderStatus
{
    Open,
    Shipped,
    Cancelled
}

public class CriteriaAddress
{
    public string? City { get; set; }
    public string Zip { get; set; } = "";
}

public class CriteriaOrderLine
{
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class CriteriaOrder
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = "";
    public CriteriaOrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime PlacedAt { get; set; }
    public DateTimeOffset ShippedAt { get; set; }
    public bool IsPriority { get; set; }
    public Guid CustomerId { get; set; }
    public CriteriaAddress ShipTo { get; set; } = new();
    public List<CriteriaOrderLine> Items { get; set; } = new();
    public string[] Tags { get; set; } = [];
    public int Score { get; set; }
    public int? Rating { get; set; }

    // jasperfx#869 SQL null semantics: nullable members compared with <> / negated.
    public string? Notes { get; set; }
    public int? Discount { get; set; }
    public CriteriaAddress BillTo { get; set; } = new();
}

/// <summary>
/// jasperfx#869: which Dynamic LINQ shapes Marten's LINQ provider answers correctly, refuses honestly, or
/// answers <b>silently wrong</b> — each measured against a LINQ-to-objects oracle running the same text over
/// the same documents, under three storage configurations.
/// </summary>
/// <remarks>
/// <para>
/// A shape that comes back <see cref="Verdict.Wrong" /> is the only kind that needs a store rule in
/// <c>DocumentStore.DiagnosticsCriteriaPolicy</c>: it runs, it returns rows, and nothing tells the operator
/// they are the wrong ones. Everything else either works or arrives as a
/// <see cref="DocumentCriteriaNotSupportedException" /> — and anything that arrives as some OTHER exception
/// fails this test outright, because an untranslatable shape must never surface as a raw provider error.
/// </para>
/// <para>
/// Every verdict is pinned. A change to Marten's LINQ translation that moves a shape between verdicts —
/// in particular one that turns an honest refusal into a wrong answer — fails here, and the printed matrix
/// says which.
/// </para>
/// </remarks>
public class document_store_diagnostics_criteria_shape_matrix
{
    private readonly ITestOutputHelper _output;

    public document_store_diagnostics_criteria_shape_matrix(ITestOutputHelper output)
    {
        _output = output;
    }

    public enum Verdict
    {
        /// <summary>The store returned exactly the oracle's rows (and order, when there is an ordering).</summary>
        Correct,

        /// <summary>The store refused with DocumentCriteriaNotSupportedException after the provider failed.</summary>
        Refused,

        /// <summary>The text was refused by the parser / allow-list / policy before reaching the provider.</summary>
        RefusedBeforeProvider,

        /// <summary>The store ran it and returned different rows from the oracle. Never acceptable.</summary>
        Wrong
    }

    private static readonly Guid SharedCustomer = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    /// <param name="WhenEnumsAreNames">
    /// The verdict under <c>EnumStorage.AsString</c>, where it differs: an enum stored as its name compares
    /// and orders alphabetically, so the ordering shapes over it are refused there.
    /// </param>
    /// <param name="Oracle">
    /// The C# answer, for the one kind of shape LINQ to objects cannot evaluate: a string method on a null
    /// member throws there, where the text's intent (null contains nothing) is unambiguous.
    /// </param>
    public sealed record Shape(string Name, string? Where, object?[]? Args, Verdict Expected, string? OrderBy = null,
        Verdict? WhenEnumsAreNames = null, Func<CriteriaOrder, bool>? Oracle = null)
    {
        public override string ToString() => Name;
    }

    private static readonly DateTime Feb3 = new(2025, 2, 3, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The measured matrix, as of jasperfx#869 against Marten at HEAD.</summary>
    public static readonly Shape[] Shapes =
    [
        // dates: member access
        new("date .Year", "PlacedAt.Year = @0", [2025], Verdict.Refused),
        new("date .Month", "PlacedAt.Month = @0", [3], Verdict.Refused),
        new("date .Day", "PlacedAt.Day = @0", [5], Verdict.Refused),
        new("date .Date", "PlacedAt.Date = @0", [Feb3], Verdict.Refused),
        new("DateTimeOffset .Month", "ShippedAt.Month = @0", [3], Verdict.Refused),

        // dates: comparisons
        new("DateTime > @0", "PlacedAt > @0", [new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)], Verdict.Correct),
        new("DateTimeOffset > @0", "ShippedAt > @0", [new DateTimeOffset(2025, 3, 1, 0, 0, 0, TimeSpan.Zero)], Verdict.Correct),
        new("DateTimeOffset > @0 as text", "ShippedAt > @0", ["2025-03-01T00:00:00Z"], Verdict.Correct),

        // collection sizes
        new("List .Count property", "Items.Count > @0", [1], Verdict.Correct),
        new("array .Length property", "Tags.Length > @0", [1], Verdict.Correct),
        new("List .Count()", "Items.Count() > @0", [1], Verdict.Correct),
        new("array .Count()", "Tags.Count() > @0", [1], Verdict.Correct),

        // collection predicates
        new("Any(pred) int", "Items.Any(Quantity > @0)", [3], Verdict.Correct),
        new("Any(pred) string", "Items.Any(Sku = @0)", ["S1"], Verdict.Correct),
        new("Any(pred) decimal", "Items.Any(Price > @0)", [20m], Verdict.Correct),
        new("Any()", "Items.Any()", null, Verdict.Correct),
        new("All(pred)", "Items.All(Quantity > @0)", [0], Verdict.Correct),
        new("Tags.Any(it = x)", "Tags.Any(it = @0)", ["vip"], Verdict.Correct),
        new("Tags.Any()", "Tags.Any()", null, Verdict.Correct),
        new("Tags.Contains", "Tags.Contains(@0)", ["vip"], Verdict.Correct),

        // in-lists
        new("inline in (ints)", "Score in (1, 2, 3)", null, Verdict.Refused),
        new("inline in (strings)", "CustomerName in (\"Ann\", \"Bob\")", null, Verdict.Refused),
        new("in @0 ints", "Score in @0", [new[] { 1, 3 }], Verdict.Correct),
        new("in @0 strings", "CustomerName in @0", [new[] { "Ann", "Bob" }], Verdict.Correct),
        // Dynamic LINQ cannot close Contains over a string[] and an enum member; the parser refuses it everywhere.
        new("enum in @0 names", "Status in @0", [new[] { "Open", "Cancelled" }], Verdict.RefusedBeforeProvider),

        // decimals
        new("decimal = @0", "Total = @0", [100.50m], Verdict.Correct),
        new("decimal > @0", "Total > @0", [100m], Verdict.Correct),
        new("decimal < @0", "Total < @0", [99.99m], Verdict.Correct),
        new("decimal literal", "Total = 100.5m", null, Verdict.Correct),
        new("decimal range", "Total >= @0 and Total <= @1", [99.99m, 100.5m], Verdict.Correct),

        // enums
        new("enum by name", "Status = \"Shipped\"", null, Verdict.Correct),
        new("enum by name @0", "Status = @0", ["Shipped"], Verdict.Correct),
        new("enum by number", "Status = 1", null, Verdict.Correct),
        new("enum by number @0", "Status = @0", [1], Verdict.Correct),
        new("enum !=", "Status != \"Open\"", null, Verdict.Correct),
        new("enum > number", "Status > 0", null, Verdict.Correct, WhenEnumsAreNames: Verdict.RefusedBeforeProvider),
        new("enum >= name", "Status >= \"Shipped\"", null, Verdict.Correct, WhenEnumsAreNames: Verdict.RefusedBeforeProvider),
        new("enum < @0", "Status < @0", [2], Verdict.Correct, WhenEnumsAreNames: Verdict.RefusedBeforeProvider),

        // nested members and nulls
        new("nested member", "ShipTo.City = @0", ["Austin"], Verdict.Correct),
        new("nested = null", "ShipTo.City = null", null, Verdict.Correct),
        new("nested != null", "ShipTo.City != null", null, Verdict.Correct),
        new("nullable int = null", "Rating = null", null, Verdict.Correct),
        new("nullable int > @0", "Rating > @0", [1], Verdict.Correct),

        // strings
        new("StartsWith", "CustomerName.StartsWith(@0)", ["A"], Verdict.Correct),
        new("Contains", "CustomerName.Contains(@0)", ["nn"], Verdict.Correct),
        new("EndsWith", "CustomerName.EndsWith(@0)", ["b"], Verdict.Correct),
        new("ToLower =", "CustomerName.ToLower() = @0", ["ann"], Verdict.Correct),
        new("ToUpper StartsWith", "CustomerName.ToUpper().StartsWith(@0)", ["AN"], Verdict.Correct),
        new("string .Length", "CustomerName.Length > @0", [3], Verdict.Refused),

        // booleans, Guids, composition
        new("bool member", "IsPriority", null, Verdict.Correct),
        new("bool = true", "IsPriority = true", null, Verdict.Correct),
        new("not bool", "not IsPriority", null, Verdict.Correct),
        new("Guid = @0", "CustomerId = @0", [SharedCustomer], Verdict.Correct),
        new("Guid = @0 as text", "CustomerId = @0", [SharedCustomer.ToString().ToUpperInvariant()], Verdict.Correct),
        new("and / or / not", "(Status = \"Open\" or Total > @0) and not IsPriority", [200m], Verdict.Correct),

        // SQL three-valued logic: NULL <> 'x' is unknown, so the database drops every null row the C# text
        // keeps. Refused by DynamicQueryShapeRules.SqlNullSemantics() unless the text settles the null case.
        new("nullable string !=", "Notes != @0", ["fragile"], Verdict.RefusedBeforeProvider),
        new("not (nullable string =)", "not (Notes = @0)", ["fragile"], Verdict.RefusedBeforeProvider),
        new("not nullable string Contains", "not Notes.Contains(@0)", ["frag"], Verdict.RefusedBeforeProvider,
            Oracle: x => !(x.Notes?.Contains("frag") ?? false)),
        new("nullable int !=", "Discount != @0", [5], Verdict.RefusedBeforeProvider),
        new("nested nullable !=", "BillTo.City != @0", ["Austin"], Verdict.RefusedBeforeProvider),
        new("!= guarded: or = null", "Notes != @0 or Notes = null", ["fragile"], Verdict.Correct),
        new("!= guarded: != null and", "Notes != null and Notes != @0", ["fragile"], Verdict.Correct),
        new("nullable int != guarded", "Discount != @0 or Discount = null", [5], Verdict.Correct),
        new("nested != guarded", "BillTo.City != @0 or BillTo.City = null", ["Austin"], Verdict.Correct),
        new("non-nullable string !=", "CustomerName != @0", ["Ann"], Verdict.Correct),
        new("not over non-nullable", "not (CustomerName = @0)", ["Ann"], Verdict.Correct),

        // refused before any provider sees it
        new("np()", "np(ShipTo.City) = null", null, Verdict.RefusedBeforeProvider),
        new("static member", "PlacedAt > DateTime.UtcNow", null, Verdict.RefusedBeforeProvider),

        // orderings (compared in sequence, tie-broken by id on both sides)
        new("order decimal desc", null, null, Verdict.Correct, "Total desc"),
        new("order nested then decimal", null, null, Verdict.Correct, "ShipTo.City, Total"),
        new("order enum", null, null, Verdict.Correct, "Status desc", Verdict.RefusedBeforeProvider),
        new("order nullable nested", null, null, Verdict.Correct, "ShipTo.City"),
        new("order nullable nested desc", null, null, Verdict.Correct, "ShipTo.City desc"),
        new("order nullable int", null, null, Verdict.Correct, "Rating"),
        new("order nullable int desc", null, null, Verdict.Correct, "Rating desc"),
        new("order string", null, null, Verdict.Correct, "CustomerName"),
        new("order bool", null, null, Verdict.Correct, "IsPriority desc"),
        new("order Guid", null, null, Verdict.Correct, "CustomerId"),
        new("order DateTime", null, null, Verdict.Correct, "PlacedAt"),
        new("order date .Year", null, null, Verdict.Refused, "PlacedAt.Year desc"),
        new("order collection Count", null, null, Verdict.Correct, "Items.Count desc"),
        new("where + order", "Total > @0", [50m], Verdict.Correct, "Score desc, Total"),
        new("collection Any + order", "Items.Any(Quantity > @0)", [1], Verdict.Correct, "Total desc"),
        new("Tags.Contains + order + paging-sized", "Tags.Contains(@0) or Items.Count() = 0", ["vip"], Verdict.Correct, "ShipTo.City desc, Score")
    ];

    public static IEnumerable<object[]> Configurations() =>
    [
        ["pascal_int_enums"],
        ["camel_string_enums"],
        ["duplicated_fields"]
    ];

    private static void configure(string configuration, StoreOptions opts)
    {
        switch (configuration)
        {
            case "pascal_int_enums":
                // Marten's defaults, restated: PascalCase members and enums stored as integers.
                opts.UseSystemTextJsonForSerialization(EnumStorage.AsInteger, Casing.Default);
                opts.Schema.For<CriteriaOrder>();
                break;

            case "camel_string_enums":
                opts.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase);
                opts.Schema.For<CriteriaOrder>();
                break;

            case "duplicated_fields":
                // Duplicated columns change what the provider reads a member FROM (the column, not the
                // JSON), so every shape over these members is re-measured against a different SQL path.
                // PlacedAt as timestamptz: Marten cannot STORE a UTC DateTime in the default
                // timestamp-without-time-zone duplicate at all, and this is also the arm the diagnostics
                // DateTime adapter must leave alone (Npgsql wants the UTC value there).
                opts.Schema.For<CriteriaOrder>()
                    .Duplicate(x => x.CustomerName)
                    .Duplicate(x => x.Status)
                    .Duplicate(x => x.Total)
                    .Duplicate(x => x.PlacedAt, pgType: "timestamp with time zone", dbType: NpgsqlDbType.TimestampTz)
                    .Duplicate(x => x.IsPriority);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(configuration));
        }
    }

    public static List<CriteriaOrder> Seed()
    {
        string[] names = ["Ann", "Bob", "ann", "Carl", "Bea", "Dan", "Eve", "Al", "Zed", "Bob", "Cy", "Anna"];
        decimal[] totals = [100.50m, 99.99m, 100m, 1000m, 0.01m, 250.25m, 100.5m, 7m, 99.995m, 300m, 100.49m, 12.5m];
        string?[] cities = ["Austin", "Boston", "austin", "Chicago"];
        string[][] tags = [[], ["vip"], ["vip", "new"], ["new"], ["gold", "vip"], []];

        return Enumerable.Range(0, 12).Select(i => new CriteriaOrder
        {
            Id = Guid.NewGuid(),
            CustomerName = names[i],
            Status = (CriteriaOrderStatus)(i % 3),
            Total = totals[i],
            PlacedAt = new DateTime(2024 + i % 3, 1 + i % 12, 1 + i * 2 % 28, i, 30, 0, DateTimeKind.Utc),
            ShippedAt = new DateTimeOffset(2025, 1 + i % 6, 10, 12, 0, 0, TimeSpan.FromHours(i % 2 == 0 ? 0 : -5)),
            IsPriority = i % 4 == 0,
            CustomerId = i % 4 == 1 ? SharedCustomer : Guid.NewGuid(),
            ShipTo = new CriteriaAddress { City = i % 5 == 0 ? null : cities[i % 4], Zip = $"{70000 + i}" },
            Items = Enumerable.Range(1, i % 4)
                .Select(j => new CriteriaOrderLine { Sku = $"S{(i + j) % 5}", Quantity = j * i % 7, Price = 5m * j + i })
                .ToList(),
            Tags = tags[i % tags.Length],
            Score = i % 5,
            Rating = i % 3 == 0 ? null : i % 4,
            Notes = i % 4 == 0 ? "fragile" : i % 4 == 1 ? "glass" : null,
            Discount = i % 3 == 0 ? null : i % 2 == 0 ? 5 : 10,
            BillTo = new CriteriaAddress { City = i % 3 == 0 ? null : i % 2 == 0 ? "Austin" : "Denver", Zip = "1" }
        }).ToList();
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task every_shape_is_correct_or_refused_never_silently_wrong(string configuration)
    {
        var schema = $"diag869_matrix_{configuration}";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.DisableNpgsqlLogging = true;
            configure(configuration, opts);
        });

        var orders = Seed();
        await using (var session = store.LightweightSession())
        {
            session.Store(orders.ToArray());
            await session.SaveChangesAsync();
        }

        var diagnostics = (IDocumentStoreDiagnostics)store;
        var typeName = typeof(CriteriaOrder).FullNameInCode();

        var table = new StringBuilder();
        table.AppendLine($"| {configuration} shape | text | oracle n | store | verdict |");
        table.AppendLine("|---|---|---|---|---|");

        var mismatches = new List<string>();

        foreach (var shape in Shapes)
        {
            var (oracleIds, oracleNote) = runOracle(orders, shape);

            Verdict verdict;
            string storeNote;
            try
            {
                var result = await diagnostics.QueryDocumentsAsync(typeName,
                    new DocumentQueryOptions(1, 100)
                    {
                        Where = shape.Where, OrderBy = shape.OrderBy, Arguments = shape.Args
                    }, CancellationToken.None);

                var storeIds = result.Documents.Select(x => Guid.Parse(x.Id)).ToList();
                storeNote = $"{storeIds.Count} (total {result.TotalCount})";

                var sameRows = oracleIds != null && (shape.OrderBy == null
                    ? storeIds.OrderBy(x => x).SequenceEqual(oracleIds.OrderBy(x => x))
                    : storeIds.SequenceEqual(oracleIds));

                verdict = sameRows && result.TotalCount == storeIds.Count ? Verdict.Correct : Verdict.Wrong;
            }
            catch (DocumentCriteriaNotSupportedException e)
            {
                // A refusal the allow-list or parser produced carries no provider exception underneath it.
                verdict = e.InnerException is DynamicQueryException ? Verdict.RefusedBeforeProvider : Verdict.Refused;
                storeNote = "refused: " + shorten(e.Message);
            }

            var text = shape.Where ?? $"order by {shape.OrderBy}";
            if (shape.Where != null && shape.OrderBy != null) text += $" order by {shape.OrderBy}";

            table.AppendLine(
                $"| {shape.Name} | `{text}` | {oracleIds?.Count.ToString() ?? oracleNote} | {storeNote} | {verdict} |");

            var expected = configuration == "camel_string_enums" ? shape.WhenEnumsAreNames ?? shape.Expected : shape.Expected;
            if (verdict != expected)
            {
                mismatches.Add($"{shape.Name}: expected {expected}, measured {verdict} ({storeNote})");
            }
        }

        _output.WriteLine(table.ToString());

        mismatches.ShouldBeEmpty(string.Join(Environment.NewLine, mismatches));
    }

    internal static (List<Guid>? Ids, string Note) runOracle(List<CriteriaOrder> orders, Shape shape)
    {
        // The same text through the same helper, over LINQ to objects, with the same id tie-breaker the
        // store applies after any ordering.
        if (shape.Oracle != null)
        {
            return (orders.Where(shape.Oracle).Select(x => x.Id).ToList(), "");
        }

        var orderBy = shape.OrderBy == null ? null : $"{shape.OrderBy}, Id";
        try
        {
            return (DynamicQuery.Apply(orders.AsQueryable(), new DynamicQueryText(shape.Where, orderBy, shape.Args))
                .Select(x => x.Id).ToList(), "");
        }
        catch (DynamicQueryException)
        {
            return (null, "parser refuses");
        }
    }

    /// <summary>
    /// The negative control for every store rule: each shape the matrix expects to be refused by a rule (and
    /// that the oracle can answer) is run through Marten's RAW provider -- the same text, composed with
    /// JasperFx's default policy only, none of Marten's rules -- and must come back different from the oracle.
    /// A shape that comes back right here no longer needs its rule, and the rule is then refusing a correct
    /// answer.
    /// </summary>
    [Theory]
    [MemberData(nameof(Configurations))]
    public async Task without_the_store_rules_each_refused_shape_is_silently_wrong(string configuration)
    {
        var schema = $"diag869_raw_{configuration}";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.DisableNpgsqlLogging = true;
            configure(configuration, opts);
        });

        var orders = Seed();
        await using (var session = store.LightweightSession())
        {
            session.Store(orders.ToArray());
            await session.SaveChangesAsync();
        }

        var controls = Shapes
            .Where(x => (configuration == "camel_string_enums" ? x.WhenEnumsAreNames ?? x.Expected : x.Expected)
                        == Verdict.RefusedBeforeProvider)
            .Select(x => (Shape: x, Oracle: runOracle(orders, x).Ids))
            .Where(x => x.Oracle != null)
            .ToList();

        controls.ShouldNotBeEmpty();

        await using var query = store.QuerySession();
        var stillRight = new List<string>();
        foreach (var (shape, oracle) in controls)
        {
            var orderBy = shape.OrderBy == null ? null : $"{shape.OrderBy}, Id";
            var raw = await DynamicQuery.Apply(query.Query<CriteriaOrder>(),
                    new DynamicQueryText(shape.Where, orderBy, shape.Args))
                .ToListAsync();
            var ids = raw.Select(x => x.Id).ToList();

            var same = shape.OrderBy == null
                ? ids.OrderBy(x => x).SequenceEqual(oracle!.OrderBy(x => x))
                : ids.SequenceEqual(oracle!);

            _output.WriteLine($"{configuration} | {shape.Name} | oracle {oracle!.Count} | raw {ids.Count} | {(same ? "SAME" : "wrong")}");
            if (same) stillRight.Add(shape.Name);
        }

        stillRight.ShouldBeEmpty("These shapes are refused by a store rule but Marten's raw provider answers them correctly");
    }

    private static string shorten(string message)
    {
        var flat = message.ReplaceLineEndings(" ").Replace("|", "/");
        return flat.Length <= 110 ? flat : flat[..110] + "…";
    }
}
