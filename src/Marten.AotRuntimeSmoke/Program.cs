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
//   projections           every Select() into a new object closed its select clause over
//                         selectExpression.Type with Activator.CreateInstance, so a NAMED DTO
//                         projection failed natively just as hard as an anonymous one, before the
//                         serializer was ever reached. An anonymous projection then still cannot be
//                         materialized by a source-generated resolver that replaced the
//                         reflection-based one, and Marten now says so with the remedy (#5624).
//   boundary aggregate    EventGraph.Build<TDoc>() closed SingleStreamProjection<,> with
//                         CloseAndBuildAs for a [BoundaryAggregate], even though both type
//                         arguments are known at compile time (TDoc, and string per jasperfx#324).
//                         ILC has no such instantiation, so every DCB boundary fetch threw
//                         "No parameterless constructor defined for type
//                         SingleStreamProjection`2[...]" (#5649).
//   DCB tags              RegisterTagType<TTag>() let JasperFx close TagTypeRegistration<TTag, TInner>
//                         with MakeGenericType and read the tag's members with ValueTypeInfo, so the
//                         store could not even be CONFIGURED. RegisterTagType<TTag, TInner>(unwrapper)
//                         closes it at compile time and reflects over nothing; the checks below run
//                         tagging, tag rules, tag queries and the consistency boundary on it, and hold
//                         the inferring overload to naming that remedy instead of failing opaquely.
//   diagnostics criteria  IDocumentStoreDiagnostics Where / OrderBy (jasperfx#869) are translated with
//                         runtime code generation, so a native binary must REFUSE them -- before any
//                         generic method is closed over the document type -- not crash, and never
//                         return the page unfiltered.
//
// Exits non-zero with the offending stack trace on the first failure, so CI reports the
// specific read path that regressed.

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using JasperFx;
using JasperFx.Documents;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Exceptions;
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
    o.Schema.For<Declaratie>();
    // #5589: names the three types so the identity generics are closed at compile time.
    o.RegisterValueTypeId<Declaratie, DeclaratieId, Guid>();
    // #5635: the scalar select clause is `where T : struct`, so a consumer enum projected on its
    // own -- Select(x => x.Soort) -- is the one projected type #5624's registry cannot close from
    // MartenLinqQueryable<T>. Marten roots its own scalar types; this names ours.
    o.RegisterScalarType<Soort>();

    o.Events.StreamIdentity = StreamIdentity.AsString;
    o.Events.AddEventType<DossierGeopend>();
    o.Events.AddEventType<RegelToegevoegd>();

    // The identity type is said out loud: the overload without it has to close
    // SingleStreamProjection<,> at runtime. Dossier is deliberately not named by a Schema.For<T>()
    // either, so this also covers the aggregate's mapping being built from a Type.
    o.Projections.LiveStreamAggregation<Dossier, string>();

    // DCB: the inner type is named and the unwrapper passed, so nothing is closed and nothing is
    // reflected over at runtime. The tag types are records over a Guid and a string, the two inner
    // types a tag table is most often keyed by.
    o.Events.AddEventType<ConsultGehouden>();
    o.Events.RegisterTagType<PatientId, Guid>(x => x.Value, "patient").ForAggregate<Behandeltraject>();
    o.Events.RegisterTagType<ZorgverlenerCode, string>(x => x.Value, "zorgverlener")
        .ForAggregate<Behandeltraject>();
    o.Events.TagWith<ConsultGehouden>(e => new ZorgverlenerCode(e.Zorgverlener));
});

DecimalOperatorRoots.Root();

var failures = 0;

try
{
    await store.Advanced.Clean.CompletelyRemoveAllAsync();

    var id = Guid.NewGuid();
    var declaratieId = new DeclaratieId(Guid.NewGuid());

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
        writing.Store(new Declaratie
        {
            Id = declaratieId, Prestatiecode = "12000", Bedrag = 42, Korting = 5
        });
        writing.Store(new Declaratie
        {
            Id = new DeclaratieId(Guid.NewGuid()), Prestatiecode = "12001", Bedrag = 7
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

    // #5579 — a strong-typed id reaches FastExpressionCompiler from two directions: JasperFx's
    // ValueTypeInfo.CreateWrapper/UnWrapper (fixed in 2.80.0) and Marten's own
    // ValueTypeIdGeneration.BuildInnerValueSource. Both are Reflection.Emit underneath, so before
    // this every one of the four checks below threw PlatformNotSupportedException from a native
    // binary -- while docs/configuration/aot-publishing.md listed strong-typed ids as AOT-clean.
    await Check("LoadAsync by a strong-typed id", async () =>
    {
        var loaded = await session.LoadAsync<Declaratie>(declaratieId);
        return loaded?.Prestatiecode == "12000";
    });

    // Reads the inner Guid back out of the wrapper to build the WHERE -- BuildInnerValueSource,
    // via StrongTypedIdMember.
    await Check("LINQ filtered on a strong-typed id", async () =>
    {
        var found = await session.Query<Declaratie>().Where(x => x.Id == declaratieId).ToListAsync();
        return found.Count == 1 && found[0].Prestatiecode == "12000";
    });

    // Selecting the id closes ValueTypeIdSelectClause<TOuter,TInner>, which wraps the raw Guid
    // coming off the reader back into the strong type -- the CreateWrapper half.
    // #5597 — a decimal comparison. Expression.GreaterThan has no IL operator for decimal, so it looks
    // up System.Decimal.op_GreaterThan reflectively, and the expression tree is built by the CONSUMER's
    // compiler-generated code before Marten sees anything.
    await Check("LINQ with a decimal comparison", async () =>
    {
        var found = await session.Query<Declaratie>().Where(x => x.Bedrag > 10).ToListAsync();
        return found.Count == 1;
    });

    await Check("LINQ selecting a strong-typed id", async () =>
    {
        var ids = await session.Query<Declaratie>().Where(x => x.Prestatiecode == "12000").Select(x => x.Id).ToListAsync();
        return ids.Count == 1 && ids[0] == declaratieId;
    });

    // #5624 — every one of the projection shapes below threw
    // "MissingMethodException: No parameterless constructor defined for type
    // 'SelectDataSelectClause`1[...]'" from a native binary, because the parser only has
    // selectExpression.Type and closed the clause over it with Activator.CreateInstance.
    await Check("LINQ projection into a NAMED type", async () =>
    {
        var summaries = await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910")
            .Select(x => new PraktijkSamenvatting { AgbCode = x.AgbCode, Naam = x.Naam }).ToListAsync();
        return summaries.Count == 1 && summaries[0].Naam == "Praktijk Jansen";
    });

    await Check("GroupBy into a NAMED type", async () =>
    {
        var tallies = await session.Query<Praktijk>().GroupBy(x => x.Soort)
            .Select(g => new SoortTally { Soort = g.Key, Count = g.Count() }).ToListAsync();
        return tallies.Count == 2;
    });

    await Check("scalar projection (string)", async () =>
    {
        var names = await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910").Select(x => x.Naam)
            .ToListAsync();
        return names.Count == 1 && names[0] == "Praktijk Jansen";
    });

    await Check("scalar projection (decimal)", async () =>
    {
        var amounts = await session.Query<Declaratie>().Where(x => x.Prestatiecode == "12000")
            .Select(x => x.Bedrag).ToListAsync();
        return amounts.Count == 1 && amounts[0] == 42m;
    });

    await Check("collection member projection", async () =>
    {
        var regels = await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910").Select(x => x.Regels)
            .ToListAsync();
        return regels.Count == 1 && regels[0].Count == 1;
    });

    await Check("SelectMany over a child collection", async () =>
    {
        var regels = await session.Query<Praktijk>().SelectMany(x => x.Regels).ToListAsync();
        return regels.Count == 1;
    });

    // #5635: the scalar shapes whose clause or selector is `where T : struct`. The enum ones need
    // the RegisterScalarType<Soort>() above; the nullable decimal needs nothing -- its gap was the
    // reflectively-closed NullableSelector<decimal> / ListQueryHandler<decimal?> pair, which the
    // registry now carries for every scalar type Marten knows.
    await Check("enum scalar projection, list", async () =>
    {
        var soorten = await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910")
            .Select(x => x.Soort).ToListAsync();
        return soorten.Count == 1 && soorten[0] == Soort.Huisarts;
    });

    await Check("enum scalar projection, single value", async () =>
    {
        var soort = await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910")
            .Select(x => x.Soort).FirstOrDefaultAsync();
        return soort == Soort.Huisarts;
    });

    await Check("nullable enum member projection", async () =>
    {
        var soorten = await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910")
            .Select(x => x.OptioneleSoort).ToListAsync();
        return soorten.Count == 1 && soorten[0] == Soort.Huisarts;
    });

    await Check("nullable decimal member projection", async () =>
    {
        var kortingen = await session.Query<Declaratie>().Where(x => x.Prestatiecode == "12000")
            .Select(x => x.Korting).ToListAsync();
        return kortingen.Count == 1 && kortingen[0] == 5m;
    });

    await Check("enum aggregate (Max)", async () =>
    {
        var max = await session.Query<Praktijk>().MaxAsync(x => x.Soort);
        return max == Soort.Apotheek;
    });

    // #5624 proper: the projected type is handed to the consumer's serializer, and no
    // [JsonSerializable(...)] can name an anonymous type. This store assigns TypeInfoResolver
    // directly, so there is no reflection-based resolver behind the context and the projection
    // cannot be materialized -- under a JIT every bit as much as natively. Marten says so instead
    // of letting System.Text.Json advise an impossible annotation.
    await Check("anonymous projection is refused with the remedy", async () =>
    {
        try
        {
            await session.Query<Praktijk>().Where(x => x.AgbCode == "01059910")
                .Select(x => new { x.AgbCode, x.Naam }).ToListAsync();
            return false;
        }
        catch (MartenNotSupportedException e)
        {
            return e.Message.Contains("UseTypeInfoResolver") && e.InnerException is NotSupportedException;
        }
    });

    await Check("anonymous GroupBy projection is refused with the remedy", async () =>
    {
        try
        {
            await session.Query<Praktijk>().GroupBy(x => x.Soort).Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync();
            return false;
        }
        catch (MartenNotSupportedException e)
        {
            return e.Message.Contains("UseTypeInfoResolver");
        }
    });

    // ...and the remedy that exception names, exercised from the native binary. UseTypeInfoResolver
    // combines the context with a DefaultJsonTypeInfoResolver, and that reflection fallback DOES
    // work natively for an anonymous type -- the consumer's assembly is a TrimmerRootAssembly, so
    // its metadata survives. IsReflectionEnabledByDefault is false here, which governs the resolver
    // System.Text.Json picks when none was supplied, not one Marten constructed outright.
    await Check("anonymous projection works with the resolver CHAINED", async () =>
    {
        var serializer = new Marten.Services.SystemTextJsonSerializer();
        serializer.UseTypeInfoResolver(SmokeJson.Default);

        await using var chained = DocumentStore.For(o =>
        {
            o.Connection(connection);
            o.Serializer(serializer);
            o.AutoCreateSchemaObjects = AutoCreate.None;
            o.DatabaseSchemaName = "aot_runtime_smoke";
            o.Schema.For<Praktijk>();
        });

        await using var s = chained.QuerySession();
        var summaries = await s.Query<Praktijk>().Select(x => new { x.AgbCode, x.Naam }).ToListAsync();
        return summaries.Count == 2;
    });

    var diagnostics = (IDocumentStoreDiagnostics)store;

    await Check("diagnostics page without criteria", async () =>
    {
        var page = await diagnostics.QueryDocumentsAsync(typeof(Praktijk).FullName!, new DocumentQueryOptions(1, 10));
        return page.TotalCount == 2;
    });

    await Check("diagnostics criteria refused under Native AOT", async () =>
    {
        var criteria = new DocumentQueryOptions(1, 10) { Where = "Naam = @0", Arguments = ["Praktijk Jansen"] };
        try
        {
            var page = await diagnostics.QueryDocumentsAsync(typeof(Praktijk).FullName!, criteria);

            // Only a JIT process (`dotnet run` of this project) may answer; a native binary must refuse.
            return RuntimeFeature.IsDynamicCodeSupported && page.TotalCount == 1;
        }
        catch (DocumentCriteriaNotSupportedException e)
        {
            return !RuntimeFeature.IsDynamicCodeSupported && e.Criterion == nameof(DocumentQueryOptions.Where);
        }
    });

    // #5649. Exercised directly rather than through AggregateByTagsAsync, because this aggregate's
    // tag type is deliberately not registered at all. This is the one site that fix moves, and it is
    // the site the reported MissingMethodException came from; the DCB checks below reach the same
    // branch through the fetch itself, on explicit RegisterTagType<TTag, TInner> registrations.
    await Check("boundary aggregate aggregator source", () =>
    {
        var source = store.Events.Build<Toegangsgrens>();
        return Task.FromResult(source is Marten.Events.Aggregation.SingleStreamProjection<Toegangsgrens, string>);
    });

    await CheckDcbAsync();
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

    // #5635: Zorgvorm is deliberately NOT registered, unlike Soort on the main store. A native
    // binary cannot close the scalar clause over it, and the bare Activator.CreateInstance message
    // -- "No parameterless constructor defined for type 'NewScalarSelectClause`1[Zorgvorm]'" --
    // tells the consumer nothing. Marten names the one line that fixes it. A JIT still answers the
    // query, so either outcome passes here; a RAW MissingMethodException fails both ways.
    await Check("an unregistered enum scalar projection names RegisterScalarType", async () =>
    {
        try
        {
            var zorgvormen = await session.Query<Aanlevering>().Select(x => x.Zorgvorm).ToListAsync();
            return zorgvormen.Count == 1 && zorgvormen[0] == Zorgvorm.Apotheek;
        }
        catch (MartenNotSupportedException e)
        {
            return e.Message.Contains("RegisterScalarType<Zorgvorm>()");
        }
    });
}

// Dynamic Consistency Boundary tags, all from the explicit RegisterTagType<TTag, TInner> registrations
// in the store above.
async Task CheckDcbAsync()
{
    var patient = new PatientId(Guid.NewGuid());
    var zorgverlener = new ZorgverlenerCode("01059910");
    var traject = new EventTagQuery().Or<PatientId>(patient);

    ConsultGehouden Consult(string verrichting) => new(patient.Value, zorgverlener.Value, verrichting);

    await Check("DCB append with tags", async () =>
    {
        await using var s = store.LightweightSession();
        var eerste = s.Events.BuildEvent(Consult("intake"));
        eerste.WithTag(patient); // the zorgverlener tag comes from the TagWith rule
        s.Events.Append("traject-1", eerste);
        await s.SaveChangesAsync();
        return true;
    });

    await Check("DCB query by a Guid-backed tag", async () =>
    {
        await using var s = store.LightweightSession();
        return (await s.Events.QueryByTagsAsync(traject)).Count == 1;
    });

    await Check("DCB query by a string-backed tag added by a TagWith rule", async () =>
    {
        await using var s = store.LightweightSession();
        return (await s.Events.QueryByTagsAsync(new EventTagQuery().Or<ZorgverlenerCode>(zorgverlener))).Count == 1
               && await s.Events.EventsExistAsync(new EventTagQuery().Or<ZorgverlenerCode>(zorgverlener));
    });

    await Check("DCB FetchForWritingByTags into a [BoundaryAggregate] and append", async () =>
    {
        await using var s = store.LightweightSession();
        var boundary = await s.Events.FetchForWritingByTags<Behandeltraject>(traject);
        if (boundary.Aggregate?.Consulten != 1) return false;

        var vervolg = s.Events.BuildEvent(Consult("controle"));
        vervolg.WithTag(patient);
        boundary.AppendOne(vervolg);
        await s.SaveChangesAsync();

        await using var q = store.LightweightSession();
        return (await q.Events.AggregateByTagsAsync<Behandeltraject>(traject))?.Consulten == 2;
    });

    await Check("DCB boundary refuses a concurrent append", async () =>
    {
        await using var first = store.LightweightSession();
        await using var second = store.LightweightSession();
        var b1 = await first.Events.FetchForWritingByTags<Behandeltraject>(traject);
        var b2 = await second.Events.FetchForWritingByTags<Behandeltraject>(traject);

        var e1 = first.Events.BuildEvent(Consult("telefonisch"));
        e1.WithTag(patient);
        b1.AppendOne(e1);
        await first.SaveChangesAsync();

        var e2 = second.Events.BuildEvent(Consult("telefonisch"));
        e2.WithTag(patient);
        b2.AppendOne(e2);
        try
        {
            await second.SaveChangesAsync();
            return false;
        }
        catch (DcbConcurrencyException)
        {
            return true;
        }
    });

    // The inferring overload cannot work natively, so it has to say what to write instead. A JIT process
    // (`dotnet run` of this project) closes the generic and simply registers it. Neither RuntimeFeature flag
    // tells the two apart here -- PublishAot turns IsDynamicCodeSupported off under `dotnet run` as well, and
    // CoreCLR derives IsDynamicCodeCompiled from it -- so the check accepts either outcome, and nothing else.
    await Check("inferred tag registration names the explicit overload under Native AOT", () =>
    {
        try
        {
            var options = new StoreOptions();
            var registered = options.Events.RegisterTagType<VerwijzingId>("verwijzing").SimpleType == typeof(Guid);
            Console.WriteLine("     (inferred registration succeeded, as under a JIT)");
            return Task.FromResult(registered);
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine("     (inferred registration refused with the remedy, as natively)");
            return Task.FromResult(e.InnerException is NotSupportedException or MissingMethodException
                                       or JasperFx.Core.Reflection.InvalidValueTypeException
                                   && e.Message.Contains("RegisterTagType<VerwijzingId, Guid>(x => x.Value, \"verwijzing\")"));
        }
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

// #5597: Expression.GreaterThan on two decimals cannot use an IL comparison -- decimal has no
// primitive '>' -- so the factory looks up System.Decimal.op_GreaterThan by reflection. The trimmer
// has no reason to keep an operator nothing statically calls, and the factory then reports it as
// "not defined". Rooting decimal's public methods keeps the metadata it needs.
internal static class DecimalOperatorRoots
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(decimal))]
    internal static void Root()
    {
    }
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

/// <summary>
/// #5579. A plain readonly record struct id -- deliberately not a Vogen or StronglyTypedId generated
/// one, so this gate tests Marten's value-type identity path and not a third-party generator's AOT
/// story.
/// </summary>
public readonly record struct DeclaratieId(Guid Value);

public class Declaratie
{
    public DeclaratieId Id { get; set; }
    public string Prestatiecode { get; set; } = "";
    public decimal Bedrag { get; set; }
    public decimal? Korting { get; set; }
}

/// <summary>
/// #5624. The nameable half of a LINQ projection: a consumer can write
/// [JsonSerializable(typeof(PraktijkSamenvatting))] for this and cannot for an anonymous type.
/// </summary>
public class PraktijkSamenvatting
{
    public string AgbCode { get; set; } = "";
    public string Naam { get; set; } = "";
}

public class SoortTally
{
    public Soort Soort { get; set; }
    public int Count { get; set; }
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

public readonly record struct PatientId(Guid Value);

public readonly record struct ZorgverlenerCode(string Value);

/// <summary>Only ever registered through the inferring overload, so nothing roots its instantiation.</summary>
public readonly record struct VerwijzingId(Guid Value);

public record ConsultGehouden(Guid Patient, string Zorgverlener, string Verrichting);

/// <summary>
/// A pure boundary aggregate over the DCB tags: no identity, so only the [BoundaryAggregate] marker gets the
/// source generator to emit its evolver, and only a tag query can load it.
/// </summary>
[BoundaryAggregate]
public class Behandeltraject
{
    public int Consulten { get; set; }

    public void Apply(ConsultGehouden _) => Consulten++;
}

// #5649: a *pure* DCB boundary aggregate -- Apply methods, no identity of any kind. The
// [BoundaryAggregate] marker is the only thing that makes EventGraph.Build<TDoc>() take the branch
// under test, and its id is unconditionally string (jasperfx#324).
[BoundaryAggregate]
public class Toegangsgrens
{
    public int Geopend { get; set; }
    public int Regels { get; set; }

    public void Apply(DossierGeopend _) => Geopend++;
    public void Apply(RegelToegevoegd _) => Regels++;
}

[JsonSerializable(typeof(Praktijk))]
[JsonSerializable(typeof(Declaratie))]
[JsonSerializable(typeof(DeclaratieId))]
[JsonSerializable(typeof(PraktijkSamenvatting))]
[JsonSerializable(typeof(SoortTally))]
[JsonSerializable(typeof(Aanlevering))]
[JsonSerializable(typeof(Dossier))]
[JsonSerializable(typeof(DossierGeopend))]
[JsonSerializable(typeof(RegelToegevoegd))]
[JsonSerializable(typeof(ConsultGehouden))]
[JsonSerializable(typeof(Behandeltraject))]
public partial class SmokeJson: JsonSerializerContext;
