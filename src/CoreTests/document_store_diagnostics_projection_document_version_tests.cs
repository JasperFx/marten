using System;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests;

public record DiagnosticsTallyIncremented;

public class DiagnosticsTally
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(DiagnosticsTallyIncremented _) => Count++;
}

/// <summary>
/// #5773: the diagnostics writer's version token on a single-stream projection document. That document's
/// <c>mt_version</c> is "the stream version this snapshot reflects", so a save through the writer leaves it where
/// it was, and a token made of <c>mt_version</c> alone never moved: a second save at the same
/// <c>ExpectedVersion</c> was accepted, a lost update. The token now also captures something every write changes.
/// The other half is what the fix must NOT do: move <c>mt_version</c> ahead of the stream, which makes the async
/// fetch's delta query (<c>version &gt; mt_version</c>) skip the next event.
/// </summary>
public class document_store_diagnostics_projection_document_version_tests: OneOffConfigurationsContext
{
    private static readonly string TallyType = typeof(DiagnosticsTally).FullNameInCode();

    private IDocumentStoreDiagnostics Diagnostics => (IDocumentStoreDiagnostics)theStore;
    private IDocumentStoreDiagnosticsWriter Writer => (IDocumentStoreDiagnosticsWriter)theStore;

    private async Task<Guid> projectedTallyAsync(bool softDeleted = false)
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<DiagnosticsTally>(SnapshotLifecycle.Async);
            if (softDeleted)
            {
                opts.Schema.For<DiagnosticsTally>().SoftDeleted();
            }

            opts.DatabaseSchemaName = softDeleted ? "diag_projected_soft" : "diag_projected";
        });

        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<DiagnosticsTally>(id, new DiagnosticsTallyIncremented());
        await session.SaveChangesAsync();

        // Stands in for the daemon, which is not running: the snapshot as the projection would write it, at the
        // stream's version.
        session.Store(new DiagnosticsTally { Id = id, Count = 1 });
        await session.SaveChangesAsync();

        return id;
    }

    private Task<DocumentWriteResult> saveAsync(Guid id, int count, string? expectedVersion)
        => Writer.SaveDocumentJsonAsync(new DocumentWriteRequest(TallyType, id.ToString(),
            theStore.Serializer.ToJson(new DiagnosticsTally { Id = id, Count = count }))
        {
            ExpectedVersion = expectedVersion
        });

    private async Task<DiagnosticsTally> appendAndFetchLatestAsync(Guid id)
    {
        await using var session = theStore.LightweightSession();
        session.Events.Append(id, new DiagnosticsTallyIncremented());
        await session.SaveChangesAsync();

        return (await session.Events.FetchLatest<DiagnosticsTally>(id)).ShouldNotBeNull();
    }

    [Fact]
    public async Task a_second_save_at_the_same_token_is_a_conflict()
    {
        var id = await projectedTallyAsync();
        var token = (await Diagnostics.LoadDocumentAsync(TallyType, id.ToString(), null))!.Version;

        (await saveAsync(id, 10, token)).Status.ShouldBe(DocumentWriteStatus.Saved);

        var second = await saveAsync(id, 20, token);
        second.Status.ShouldBe(DocumentWriteStatus.ConcurrencyConflict);
        second.Document.ShouldNotBeNull().Json.ShouldContain("10");
    }

    [Fact]
    public async Task the_token_a_save_returns_is_accepted_by_the_next_save()
    {
        var id = await projectedTallyAsync();
        var token = (await Diagnostics.LoadDocumentAsync(TallyType, id.ToString(), null))!.Version;

        var first = await saveAsync(id, 10, token);
        first.Status.ShouldBe(DocumentWriteStatus.Saved);

        (await saveAsync(id, 20, first.Document!.Version)).Status.ShouldBe(DocumentWriteStatus.Saved);
    }

    [Fact]
    public async Task a_delete_at_the_pre_edit_token_is_a_conflict()
    {
        var id = await projectedTallyAsync();
        var token = (await Diagnostics.LoadDocumentAsync(TallyType, id.ToString(), null))!.Version;

        (await saveAsync(id, 10, token)).Status.ShouldBe(DocumentWriteStatus.Saved);

        var deleted = await Writer.DeleteDocumentAsync(
            new DocumentDeleteRequest(TallyType, id.ToString()) { ExpectedVersion = token });
        deleted.Status.ShouldBe(DocumentWriteStatus.ConcurrencyConflict);
    }

    [Fact]
    public async Task an_event_appended_after_an_edit_is_still_applied_by_fetch_latest()
    {
        var id = await projectedTallyAsync();
        var token = (await Diagnostics.LoadDocumentAsync(TallyType, id.ToString(), null))!.Version;

        (await saveAsync(id, 10, token)).Status.ShouldBe(DocumentWriteStatus.Saved);

        (await appendAndFetchLatestAsync(id)).Count.ShouldBe(11);
    }

    [Fact]
    public async Task an_undelete_does_not_move_the_version_ahead_of_the_stream()
    {
        var id = await projectedTallyAsync(softDeleted: true);
        await using (var session = theStore.LightweightSession())
        {
            session.Delete<DiagnosticsTally>(id);
            await session.SaveChangesAsync();
        }

        var deleted = (await Diagnostics.LoadDocumentAsync(TallyType, id.ToString(), null))!;

        var restored = await Writer.UndeleteDocumentAsync(
            new DocumentUndeleteRequest(TallyType, id.ToString()) { ExpectedVersion = deleted.Version });
        restored.Status.ShouldBe(DocumentWriteStatus.Undeleted);
        restored.Document.ShouldNotBeNull().Version.ShouldNotBe(deleted.Version);

        (await appendAndFetchLatestAsync(id)).Count.ShouldBe(2);
    }
}
