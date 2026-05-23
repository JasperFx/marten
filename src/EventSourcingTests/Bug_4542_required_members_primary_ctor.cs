using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

// Regression for JasperFx/marten#4542: a record aggregate with a primary
// constructor for the id PLUS required members, projected by a partial
// SingleStreamProjection with conventional Create/Apply. With
// JasperFx.Events.SourceGenerator 2.0.0 the generated evolver synthesized the
// Apply-only/null-snapshot branch as `new DiagnostiekActiviteit { ... = default! }`,
// which doesn't compile for a primary-ctor record (CS7036). Fixed in
// jasperfx#359 / JasperFx.Events.SourceGenerator 2.1.0.

public record DiagnostiekActiviteit(string Id)
{
    public required Guid? SubContractorId { get; set; }
    public required string Aanlevercode { get; set; }
    public required int Prestatiecodelijst { get; set; }
}

public record CareReceived(string ProvidedCareId, Guid? SubContractorId, string Prestatiecode, int Prestatiecodelijst);

public record CareImported(string Note);

public partial class DiagnostiekActiviteitProjection : SingleStreamProjection<DiagnostiekActiviteit, string>
{
    public static DiagnostiekActiviteit Create(CareReceived e) => new(e.ProvidedCareId)
    {
        SubContractorId = e.SubContractorId,
        Aanlevercode = e.Prestatiecode,
        Prestatiecodelijst = e.Prestatiecodelijst,
    };

    public void Apply(CareImported e, DiagnostiekActiviteit a)
    {
        // CareImported only mutates an already-created snapshot
    }
}

public class Bug_4542_required_members_primary_ctor : BugIntegrationContext
{
    [Fact]
    public async Task projection_with_required_member_record_builds_and_runs()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add<DiagnostiekActiviteitProjection>(ProjectionLifecycle.Inline);
        });

        var streamId = Guid.NewGuid().ToString();
        var subContractor = Guid.NewGuid();

        theSession.Events.StartStream<DiagnostiekActiviteit>(streamId,
            new CareReceived(streamId, subContractor, "ABC123", 42),
            new CareImported("imported"));
        await theSession.SaveChangesAsync();

        var doc = await theSession.LoadAsync<DiagnostiekActiviteit>(streamId);
        doc.ShouldNotBeNull();
        doc.Id.ShouldBe(streamId);
        doc.SubContractorId.ShouldBe(subContractor);
        doc.Aanlevercode.ShouldBe("ABC123");
        doc.Prestatiecodelijst.ShouldBe(42);
    }
}
