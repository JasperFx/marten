#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

// Regression for JasperFx/marten#4543: an aggregate type used as a *nullable*
// parameter decorated with an [ReadAggregate]-style attribute (anything that
// implements IRefersToAggregate) made JasperFx.Events.SourceGenerator 2.0.0
// feed the `?` annotation into the generated hint name, which Roslyn rejects —
// CS8785 aborting the WHOLE generator pass, so no evolvers were emitted for the
// assembly. Fixed in jasperfx#359 / JasperFx.Events.SourceGenerator 2.1.0 by
// sanitizing hint names + normalizing nullable annotations.
//
// The real attribute lives in Wolverine.Http; a local marker attribute
// implementing IRefersToAggregate exercises the identical source-generator path.

public class ReadAggregateAttribute(string memberName) : Attribute, IRefersToAggregate
{
    public string MemberName { get; } = memberName;
}

public record EigenPrestatie
{
    public required string Id { get; init; }
    public required string Prestatiecode { get; init; }

    public static EigenPrestatie Create(EigenPrestatieAangemaakt e) => new()
    {
        Id = e.PrestatieId,
        Prestatiecode = e.Prestatiecode
    };
}

public record EigenPrestatieAangemaakt(string PrestatieId, string Prestatiecode);

public record EigenTariefAanmaken(string PrestatieId);

public static class EigenTariefAanmakenEndpoint
{
    // The nullable `EigenPrestatie?` parameter is what triggered the #4543
    // hint-name crash during source generation.
    public static void Validate(
        EigenTariefAanmaken request,
        [ReadAggregate(nameof(EigenTariefAanmaken.PrestatieId))]
        EigenPrestatie? prestatie)
    {
    }
}

public class Bug_4543_nullable_read_aggregate_param : BugIntegrationContext
{
    [Fact]
    public async Task generator_does_not_crash_and_snapshot_works()
    {
        // If the source generator had crashed (CS8785), no dispatcher would be
        // generated for EigenPrestatie and this snapshot registration would fail
        // at runtime with "No source-generated dispatcher found".
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Snapshot<EigenPrestatie>(SnapshotLifecycle.Inline);
        });

        var id = Guid.NewGuid().ToString();
        theSession.Events.StartStream<EigenPrestatie>(id, new EigenPrestatieAangemaakt(id, "P-100"));
        await theSession.SaveChangesAsync();

        var doc = await theSession.LoadAsync<EigenPrestatie>(id);
        doc.ShouldNotBeNull();
        doc.Id.ShouldBe(id);
        doc.Prestatiecode.ShouldBe("P-100");
    }
}
