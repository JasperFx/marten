using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1563_user_friendly_warning_about_public_type: BugIntegrationContext
{
    [DocumentAlias("internal_doc")]
    internal class InternalDoc
    {
        public Guid Id { get; set; }
    }

    [Fact]
    public async Task internal_document_types_round_trip()
    {
        // Pre-#4404 Marten's Roslyn-emit document storage refused to
        // operate on non-public document types — the codegen had to
        // `Activator.CreateInstance` them from an external assembly.
        // This bug was originally about raising a friendly error in
        // that case. The closed-shape path uses generics + the
        // serializer, so internal types work transparently — verify the
        // round-trip succeeds where it used to throw.
        var doc = new InternalDoc { Id = Guid.NewGuid() };
        theSession.Store(doc);
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<InternalDoc>(doc.Id);
        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(doc.Id);
    }
}
