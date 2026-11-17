using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

public class OrderedNote
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
}

public abstract class ArchivedVehicle
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class ArchivedBus: ArchivedVehicle
{
    public int Seats { get; set; }
}

public class ArchivedCar: ArchivedVehicle;

/// <summary>
/// #5740: Marten's own side of jasperfx#1058 (<see cref="DocumentQueryOptions.OrderByMetadata" />) and
/// jasperfx#1059 (<see cref="IDocumentStoreDiagnosticsWriter.UndeleteDocumentAsync" />).
/// </summary>
/// <remarks>
/// <c>DocumentStoreDiagnosticsCompliance</c> pins the contract against a Guid-versioned, created-untracked,
/// single-tenanted type, so on Marten its Version and Created facts only ever see the refusal. What is asserted
/// here is the other half: the orderings Marten does honour (a numeric revision, a tracked created time), the
/// refusal on the criteria path as well as the raw one, and undelete against the shapes the compliance suite
/// does not build (conjoined tenancy, a sub-class named by its root).
/// </remarks>
public class document_store_diagnostics_metadata_ordering_and_undelete_tests: OneOffConfigurationsContext
{
    private static readonly string NoteType = typeof(OrderedNote).FullNameInCode();
    private static readonly string VehicleType = typeof(ArchivedVehicle).FullNameInCode();

    private IDocumentStoreDiagnostics Diagnostics => (IDocumentStoreDiagnostics)theStore;
    private IDocumentStoreDiagnosticsWriter Writer => (IDocumentStoreDiagnosticsWriter)theStore;

    /// <summary>
    /// Each test maps the same document types differently (a numeric revision here, a Guid version there,
    /// conjoined tenancy elsewhere), so each gets a schema of its own: Weasel does not migrate a column's type,
    /// and a shared table would keep whichever <c>mt_version</c> it was created with.
    /// </summary>
    private void Configure(string schema, Action<StoreOptions> configure)
        => StoreOptions(opts =>
        {
            configure(opts);
            opts.DatabaseSchemaName = schema;
        });

    private async Task<OrderedNote[]> NotesWrittenInTurnAsync(int count, string? tenantId = null)
    {
        var notes = Enumerable.Range(1, count).Select(i => new OrderedNote { Id = Guid.NewGuid(), Text = $"n{i}" })
            .ToArray();

        foreach (var note in notes)
        {
            await using var session = tenantId == null ? theStore.LightweightSession() : theStore.LightweightSession(tenantId);
            session.Store(note);
            await session.SaveChangesAsync();
            await Task.Delay(15);
        }

        return notes;
    }

    private async Task TouchAsync(OrderedNote note, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            await using var session = theStore.LightweightSession();
            note.Text += "!";
            session.Store(note);
            await session.SaveChangesAsync();
        }
    }

    private static Guid[] IdsOf(DocumentQueryResult result)
        => result.Documents.Select(x => Guid.Parse(x.Id)).ToArray();

    [Fact]
    public async Task a_numeric_revision_orders_both_ways()
    {
        Configure("diag_order_revision", opts => opts.Schema.For<OrderedNote>().UseNumericRevisions(true));
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(OrderedNote));

        var notes = await NotesWrittenInTurnAsync(3);
        await TouchAsync(notes[0], 2); // revision 3
        await TouchAsync(notes[1]); // revision 2, notes[2] stays at 1

        var descending = await Diagnostics.QueryDocumentsAsync(NoteType,
            new DocumentQueryOptions(1, 10)
            {
                OrderByMetadata = new DocumentMetadataOrder(DocumentMetadataField.Version, Descending: true)
            });
        IdsOf(descending).ShouldBe([notes[0].Id, notes[1].Id, notes[2].Id]);
        descending.Documents.Select(x => x.Version).ShouldBe(["3", "2", "1"]);

        var ascending = await Diagnostics.QueryDocumentsAsync(NoteType,
            new DocumentQueryOptions(1, 10) { OrderByMetadata = new DocumentMetadataOrder(DocumentMetadataField.Version) });
        IdsOf(ascending).ShouldBe([notes[2].Id, notes[1].Id, notes[0].Id]);
    }

    [Fact]
    public async Task a_tracked_created_time_orders_by_first_write_not_last()
    {
        Configure("diag_order_created", opts => opts.Schema.For<OrderedNote>().Metadata(m => m.CreatedAt.Enabled = true));
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(OrderedNote));

        var notes = await NotesWrittenInTurnAsync(3);
        await TouchAsync(notes[0]);

        var byCreated = await Diagnostics.QueryDocumentsAsync(NoteType,
            new DocumentQueryOptions(1, 10) { OrderByMetadata = new DocumentMetadataOrder(DocumentMetadataField.Created) });
        IdsOf(byCreated).ShouldBe(notes.Select(x => x.Id));

        var newestFirst = await Diagnostics.QueryDocumentsAsync(NoteType,
            new DocumentQueryOptions(1, 10) { OrderByMetadata = DocumentMetadataOrder.NewestFirst });
        IdsOf(newestFirst).ShouldBe([notes[0].Id, notes[2].Id, notes[1].Id]);
    }

    [Fact]
    public async Task an_untracked_created_time_is_refused_on_the_criteria_path_too()
    {
        // The criteria path is a separate statement builder. A refusal made only on the raw read would let a
        // Where-bearing request quietly fall back to identity order.
        Configure("diag_order_plain", opts => opts.Schema.For<OrderedNote>());
        await NotesWrittenInTurnAsync(1);

        var ex = await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(() =>
            Diagnostics.QueryDocumentsAsync(NoteType,
                new DocumentQueryOptions(1, 10)
                {
                    Where = "Text != @0",
                    Arguments = ["nothing"],
                    OrderByMetadata = new DocumentMetadataOrder(DocumentMetadataField.Created)
                }));

        ex.Criterion.ShouldBe(nameof(DocumentQueryOptions.OrderByMetadata));
    }

    [Fact]
    public async Task a_guid_version_is_refused_rather_than_ordered()
    {
        Configure("diag_order_guid", opts => opts.Schema.For<OrderedNote>().UseOptimisticConcurrency(true));
        await NotesWrittenInTurnAsync(1);

        var ex = await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(() =>
            Diagnostics.QueryDocumentsAsync(NoteType,
                new DocumentQueryOptions(1, 10)
                {
                    OrderByMetadata = new DocumentMetadataOrder(DocumentMetadataField.Version)
                }));

        ex.Criterion.ShouldBe(nameof(DocumentQueryOptions.OrderByMetadata));
    }

    [Fact]
    public async Task metadata_ordering_under_all_tenants_still_leads_with_the_tenant()
    {
        Configure("diag_order_tenants", opts => opts.Schema.For<OrderedNote>().MultiTenanted());
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(OrderedNote));

        // Interleaved across tenants, so newest-first alone would alternate them.
        var older = await NotesWrittenInTurnAsync(1, "red");
        var middle = await NotesWrittenInTurnAsync(1, "blue");
        var newer = await NotesWrittenInTurnAsync(1, "red");
        var newest = await NotesWrittenInTurnAsync(1, "blue");

        var result = await Diagnostics.QueryDocumentsAsync(NoteType,
            new DocumentQueryOptions(1, 10) { AllTenants = true, OrderByMetadata = DocumentMetadataOrder.NewestFirst });

        result.Documents.Select(x => x.TenantId).ShouldBe(["blue", "blue", "red", "red"]);
        IdsOf(result).ShouldBe([newest[0].Id, middle[0].Id, newer[0].Id, older[0].Id]);
    }

    [Fact]
    public async Task undelete_restores_only_the_named_tenants_row()
    {
        Configure("diag_undelete_tenants", opts => opts.Schema.For<OrderedNote>().MultiTenanted().SoftDeleted());
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(OrderedNote));

        // The same id in two tenants, both soft-deleted.
        var id = Guid.NewGuid();
        foreach (var tenant in new[] { "red", "blue" })
        {
            await using var session = theStore.LightweightSession(tenant);
            session.Store(new OrderedNote { Id = id, Text = tenant });
            await session.SaveChangesAsync();
            session.Delete<OrderedNote>(id);
            await session.SaveChangesAsync();
        }

        var result = await Writer.UndeleteDocumentAsync(
            new DocumentUndeleteRequest(NoteType, id.ToString()) { TenantId = "red" });

        result.Status.ShouldBe(DocumentWriteStatus.Undeleted);
        result.Document.ShouldNotBeNull().TenantId.ShouldBe("red");

        (await Diagnostics.LoadDocumentAsync(NoteType, id.ToString(), "red")).ShouldNotBeNull().IsDeleted.ShouldBeFalse();
        (await Diagnostics.LoadDocumentAsync(NoteType, id.ToString(), "blue")).ShouldNotBeNull().IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task undelete_through_the_root_name_restores_a_sub_class_as_itself()
    {
        Configure("diag_undelete_hierarchy", opts => opts.Schema.For<ArchivedVehicle>()
            .AddSubClass<ArchivedBus>().AddSubClass<ArchivedCar>().SoftDeleted());
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(ArchivedVehicle));

        var bus = new ArchivedBus { Id = Guid.NewGuid(), Name = "the 42", Seats = 40 };
        await using (var session = theStore.LightweightSession())
        {
            session.Store(bus);
            await session.SaveChangesAsync();
            session.Delete<ArchivedVehicle>(bus.Id);
            await session.SaveChangesAsync();
        }

        var result = await Writer.UndeleteDocumentAsync(new DocumentUndeleteRequest(VehicleType, bus.Id.ToString()));

        result.Status.ShouldBe(DocumentWriteStatus.Undeleted);
        result.Document.ShouldNotBeNull().DocumentType.ShouldBe(typeof(ArchivedBus).FullNameInCode());

        await using var query = theStore.QuerySession();
        (await query.LoadAsync<ArchivedVehicle>(bus.Id)).ShouldBeOfType<ArchivedBus>().Seats.ShouldBe(40);
    }

    [Fact]
    public async Task undelete_of_a_numerically_revisioned_document_moves_its_revision()
    {
        Configure("diag_undelete_revision", opts => opts.Schema.For<OrderedNote>().UseNumericRevisions(true).SoftDeleted());
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(OrderedNote));

        var note = new OrderedNote { Id = Guid.NewGuid(), Text = "revised" };
        await using (var session = theStore.LightweightSession())
        {
            session.Store(note);
            await session.SaveChangesAsync();
            session.Delete<OrderedNote>(note.Id);
            await session.SaveChangesAsync();
        }

        var deleted = (await Diagnostics.LoadDocumentAsync(NoteType, note.Id.ToString(), null))!;

        var result = await Writer.UndeleteDocumentAsync(
            new DocumentUndeleteRequest(NoteType, note.Id.ToString()) { ExpectedVersion = deleted.Version });

        result.Status.ShouldBe(DocumentWriteStatus.Undeleted);
        var restored = result.Document.ShouldNotBeNull();
        restored.IsDeleted.ShouldBeFalse();
        long.Parse(restored.Version!).ShouldBeGreaterThan(long.Parse(deleted.Version!));
    }
}
