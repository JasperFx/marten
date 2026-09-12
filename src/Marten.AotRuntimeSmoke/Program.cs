// Runtime AOT smoke test (marten#5328).
//
// See Marten.AotRuntimeSmoke.csproj for why this exists alongside src/Marten.AotSmoke:
// that one is a build-time analyzer gate, this one publishes natively and runs.
//
// Each check below corresponds to a MakeGenericType / Activator.CreateInstance /
// Reflection.Emit site that used to be on a document read path:
//
//   LINQ                  QuerySession.StorageFor(Type) closed StorageFinder<T> reflectively.
//   captured variable     LinqInternalExtensions.ReduceToConstant compiled a lambda with
//                         FastExpressionCompiler, i.e. Reflection.Emit.
//   captured enum         same method, but the enum-to-underlying Convert the C# compiler emits
//                         for `x.Enum == captured` was not one of the shapes it could walk
//                         reflectively, so it still reached Reflection.Emit (#5361).
//   compiled query        CompiledQueryPlan.sortMembers closed PropertyQueryMember<T>
//                         reflectively.
//   renamed enum          a query rendered the value with Enum.GetName, which is the declared name and
//                         not what the serializer stored for a [JsonStringEnumMemberName] member. It
//                         now asks the serializer - and asking reflection instead would pass here and
//                         fail from a trimmed binary, which is the whole point of this project (#5376).
//   event read            EventColumnReaders.BuildAsync closed BuildAsyncImpl<T> with
//                         MethodInfo.MakeGenericMethod, so the first event any read brought back
//                         threw - every AggregateStreamAsync, FetchForWriting and projection.
//   live aggregation      Projections.LiveStreamAggregation<T>() closed SingleStreamProjection<,>
//                         on an identity type only known at runtime, and validating it closed
//                         DocumentMappingBuilder<> over the aggregate.
//   child collection      a containment filter serialized its jsonb payload through the consumer's
//                         source-generated resolver, which carries documents and not object[],
//                         Dictionary<string, object> or the enum being compared (#5374).
//
// Exits non-zero with the offending stack trace on the first failure, so CI reports the
// specific read path that regressed.

using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Linq;
using Weasel.Core;

[assembly: JasperFxAssembly]

var connection = Environment.GetEnvironmentVariable("marten_testing_database")
                 ?? "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres";

var store = DocumentStore.For(o =>
{
    o.Connection(connection);
    // Reflection-based serialization is disabled under PublishAot, so the source-generated
    // resolver below is part of the supported recipe rather than a smoke-test shortcut.
    o.UseSystemTextJsonForSerialization(new JsonSerializerOptions { TypeInfoResolver = SmokeJson.Default });
    o.AutoCreateSchemaObjects = AutoCreate.All;
    o.DatabaseSchemaName = "aot_runtime_smoke";
    o.Schema.For<Praktijk>().Index(x => x.AgbCode);

    o.Events.StreamIdentity = StreamIdentity.AsString;
    o.Events.AddEventType<DossierGeopend>();
    o.Events.AddEventType<RegelToegevoegd>();

    // The identity type is said out loud: the overload without it has to close
    // SingleStreamProjection<,> at runtime. Dossier is deliberately not named by a Schema.For<T>()
    // either, so this also covers the aggregate's mapping being built from a Type.
    o.Projections.LiveStreamAggregation<Dossier, string>();
});

var failures = 0;

try
{
    await store.Advanced.Clean.CompletelyRemoveAllAsync();

    var id = Guid.NewGuid();

    await using (var writing = store.LightweightSession())
    {
        writing.Store(new Praktijk
        {
            Id = id, AgbCode = "01059910", Naam = "Praktijk Jansen", Soort = Soort.Huisarts,
            OptioneleSoort = Soort.Huisarts,
            Regels = [new Regel { Prestatiecode = "12000", Soort = Soort.Huisarts }]
        });
        writing.Store(new Praktijk
        {
            Id = Guid.NewGuid(), AgbCode = "01059911", Naam = "Praktijk Pietersen", Soort = Soort.Apotheek
        });
        writing.Events.StartStream<Dossier>("dossier-1",
            new DossierGeopend("dossier-1", "Dossier Jansen"), new RegelToegevoegd("12000"));
        await writing.SaveChangesAsync();
    }

    Console.WriteLine("OK   write + schema creation");

    await using var session = store.QuerySession();

    await Check("LoadAsync", async () =>
    {
        var loaded = await session.LoadAsync<Praktijk>(id);
        return loaded?.Naam == "Praktijk Jansen";
    });

    await Check("LINQ with a literal", async () =>
    {
        var found = await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910").ToListAsync();
        return found.Count == 1;
    });

    await Check("LINQ with a captured variable", async () =>
    {
        var captured = "01059910";
        var found = await session.Query<Praktijk>().Where(x => x.AgbCode == captured).ToListAsync();
        return found.Count == 1;
    });

    await Check("LINQ with an enum literal", async () =>
    {
        var found = await session.Query<Praktijk>().Where(x => x.Soort == Soort.Huisarts).ToListAsync();
        return found.Count == 1;
    });

    // #5361 — this is the one that threw where the literal above did not.
    await Check("LINQ with an enum in a captured variable", async () =>
    {
        var soort = Soort.Huisarts;
        var found = await session.Query<Praktijk>().Where(x => x.Soort == soort).ToListAsync();
        return found.Count == 1;
    });

    await Check("LINQ with a nullable enum in a captured variable", async () =>
    {
        Soort? soort = Soort.Huisarts;
        var found = await session.Query<Praktijk>().Where(x => x.OptioneleSoort == soort).ToListAsync();
        return found.Count == 1;
    });

    await Check("LINQ with an enum comparison operator", async () =>
    {
        var soort = Soort.Apotheek;
        var found = await session.Query<Praktijk>().Where(x => x.Soort < soort).ToListAsync();
        return found.Count == 1;
    });

    await Check("LINQ with StartsWith + OrderBy", async () =>
    {
        var found = await session.Query<Praktijk>().Where(x => x.Naam.StartsWith("Praktijk"))
            .OrderBy(x => x.Naam).ToListAsync();
        return found.Count == 2 && found[0].Naam == "Praktijk Jansen";
    });

    await Check("LINQ with IsOneOf", async () =>
    {
        var codes = new[] { "01059910", "99999999" };
        var found = await session.Query<Praktijk>().Where(x => x.AgbCode.IsOneOf(codes)).ToListAsync();
        return found.Count == 1;
    });

    await Check("LINQ aggregate", async () => await session.Query<Praktijk>().CountAsync() == 2);

    await Check("raw SQL", async () =>
    {
        var found = await session.QueryAsync<Praktijk>("where data ->> 'AgbCode' = ?", "01059910");
        return found.Count == 1;
    });

    await Check("compiled query", async () =>
    {
        var found = await session.QueryAsync(new PraktijkByAgb { AgbCode = "01059910" });
        return found.Count() == 1;
    });

    await CheckRenamedEnumMemberAsync();

    // Reading an event is a separate path from reading a document: the events table hands its
    // columns to the event through reader delegates of its own.
    await Check("event stream read + live aggregation", async () =>
    {
        var dossier = await session.Events.AggregateStreamAsync<Dossier>("dossier-1");
        return dossier?.Naam == "Dossier Jansen" && dossier.Regels == 1;
    });

    await Check("raw event query", async () =>
    {
        var events = await session.Events.QueryAllRawEvents().ToListAsync();
        return events.Count == 2 && events[0].StreamKey == "dossier-1";
    });

    // #5374 — a filter over a child collection is a jsonb containment query, and its payload is
    // Marten's own dictionary rather than a document the consumer's resolver knows.
    await Check("child collection filter", async () =>
    {
        var prestatiecode = "12000";
        var found = await session.Query<Praktijk>()
            .Where(x => x.Regels.Any(r => r.Prestatiecode == prestatiecode)).ToListAsync();
        return found.Count == 1;
    });

    await Check("child collection filter over an enum", async () =>
    {
        var soort = Soort.Huisarts;
        var found = await session.Query<Praktijk>()
            .Where(x => x.Regels.Any(r => r.Soort == soort)).ToListAsync();
        return found.Count == 1;
    });

}
catch (Exception e)
{
    Console.Error.WriteLine("FAIL setup");
    Console.Error.WriteLine(e);
    return 1;
}

if (failures > 0)
{
    Console.Error.WriteLine($"Marten AOT runtime smoke FAILED — {failures} read path(s) broken under Native AOT.");
    return 1;
}

Console.WriteLine("Marten AOT runtime smoke OK — every document and event read path ran from a native binary.");
return 0;

// A store of its own, because the renamed member only differs from its declared name when enums are
// stored as strings. #5376: the equality path and every in / Contains shape have to agree, and the
// latter render through Weasel's EnumIsOneOf fragments rather than through EnumAsStringMember.
async Task CheckRenamedEnumMemberAsync()
{
    await using var store = DocumentStore.For(o =>
    {
        o.Connection(connection);
        o.UseSystemTextJsonForSerialization(EnumStorage.AsString,
            configure: json => json.TypeInfoResolver = SmokeJson.Default);
        o.AutoCreateSchemaObjects = AutoCreate.All;
        o.DatabaseSchemaName = "aot_runtime_smoke_strings";
        o.Schema.For<Aanlevering>();
    });

    await store.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Aanlevering));

    var id = Guid.NewGuid();

    await using (var writing = store.LightweightSession())
    {
        writing.Store(new Aanlevering { Id = id, Zorgvorm = Zorgvorm.Apotheek });
        await writing.SaveChangesAsync();
    }

    await using var session = store.QuerySession();

    await Check("query a renamed enum member", async () =>
    {
        var found = await session.Query<Aanlevering>().Where(x => x.Zorgvorm == Zorgvorm.Apotheek).ToListAsync();
        return found.Count == 1 && found[0].Id == id;
    });

    await Check("in over a renamed enum member", async () =>
    {
        var found = await session.Query<Aanlevering>()
            .Where(x => x.Zorgvorm.In(Zorgvorm.Apotheek)).ToListAsync();
        return found.Count == 1 && found[0].Id == id;
    });

    await Check("Contains over a renamed enum member", async () =>
    {
        var wanted = new[] { Zorgvorm.Apotheek };
        var found = await session.Query<Aanlevering>()
            .Where(x => wanted.Contains(x.Zorgvorm)).ToListAsync();
        return found.Count == 1 && found[0].Id == id;
    });
}

async Task Check(string description, Func<Task<bool>> check)
{
    try
    {
        if (await check())
        {
            Console.WriteLine($"OK   {description}");
            return;
        }

        Console.Error.WriteLine($"FAIL {description} — ran, but returned the wrong result");
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"FAIL {description}");
        Console.Error.WriteLine(e);
    }

    failures++;
}

public class Praktijk
{
    public Guid Id { get; set; }
    public string AgbCode { get; set; } = "";
    public string Naam { get; set; } = "";
    public Soort Soort { get; set; }
    public Soort? OptioneleSoort { get; set; }
    public List<Regel> Regels { get; set; } = [];
}

public class Regel
{
    public string Prestatiecode { get; set; } = "";
    public Soort Soort { get; set; }
}

public enum Soort
{
    Huisarts,
    Apotheek
}

public class Aanlevering
{
    public Guid Id { get; set; }
    public Zorgvorm Zorgvorm { get; set; }
}

public enum Zorgvorm
{
    Huisarts,

    [JsonStringEnumMemberName("apotheek-houdend")]
    Apotheek
}

public class PraktijkByAgb: ICompiledListQuery<Praktijk>
{
    public string AgbCode { get; set; } = "";

    public Expression<Func<IMartenQueryable<Praktijk>, IEnumerable<Praktijk>>> QueryIs() =>
        q => q.Where(x => x.AgbCode == AgbCode);
}

public class Dossier
{
    public string Id { get; set; } = "";
    public string Naam { get; set; } = "";
    public int Regels { get; set; }

    public static Dossier Create(DossierGeopend geopend) => new() { Id = geopend.Nummer, Naam = geopend.Naam };

    public static void Apply(RegelToegevoegd _, Dossier dossier) => dossier.Regels++;
}

public record DossierGeopend(string Nummer, string Naam);

public record RegelToegevoegd(string Prestatiecode);

[JsonSerializable(typeof(Praktijk))]
[JsonSerializable(typeof(Aanlevering))]
[JsonSerializable(typeof(Dossier))]
[JsonSerializable(typeof(DossierGeopend))]
[JsonSerializable(typeof(RegelToegevoegd))]
public partial class SmokeJson: JsonSerializerContext;
