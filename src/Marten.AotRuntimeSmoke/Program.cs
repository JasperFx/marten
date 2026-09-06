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
//   compiled query        CompiledQueryPlan.sortMembers closed PropertyQueryMember<T>
//                         reflectively.
//
// Exits non-zero with the offending stack trace on the first failure, so CI reports the
// specific read path that regressed.

using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using JasperFx;
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
});

var failures = 0;

try
{
    await store.Advanced.Clean.CompletelyRemoveAllAsync();

    var id = Guid.NewGuid();

    await using (var writing = store.LightweightSession())
    {
        writing.Store(new Praktijk { Id = id, AgbCode = "01059910", Naam = "Praktijk Jansen" });
        writing.Store(new Praktijk { Id = Guid.NewGuid(), AgbCode = "01059911", Naam = "Praktijk Pietersen" });
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

Console.WriteLine("Marten AOT runtime smoke OK — every document read path ran from a native binary.");
return 0;

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
}

public class PraktijkByAgb: ICompiledListQuery<Praktijk>
{
    public string AgbCode { get; set; } = "";

    public Expression<Func<IMartenQueryable<Praktijk>, IEnumerable<Praktijk>>> QueryIs() =>
        q => q.Where(x => x.AgbCode == AgbCode);
}

[JsonSerializable(typeof(Praktijk))]
public partial class SmokeJson: JsonSerializerContext;
