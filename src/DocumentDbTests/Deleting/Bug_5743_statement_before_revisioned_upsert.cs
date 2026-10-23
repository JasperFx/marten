using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Deleting;

/// <summary>
/// #5743. <c>UndoDeleteWhere</c> and <c>HardDeleteWhere</c> queue a bare <c>StatementOperation</c>, which was not
/// marked <c>NoDataReturnedCall</c>, unlike <c>Deletion</c> and <c>PatchOperation</c>. The batch therefore
/// called <c>NextResult</c> after the statement's UPDATE / DELETE, which produces no result set. That skipped
/// the NEXT operation's result, so a numeric-revision upsert queued after it read nothing back and reported a
/// <see cref="JasperFx.ConcurrencyException" /> for a write that had not conflicted.
/// </summary>
public class Bug_5743_statement_before_revisioned_upsert: OneOffConfigurationsContext
{
    private async Task<RevisionedNote> StoredNoteAsync()
    {
        StoreOptions(opts => opts.Schema.For<RevisionedNote>().UseNumericRevisions(true).SoftDeleted());
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(RevisionedNote));

        var note = new RevisionedNote { Id = Guid.NewGuid(), Text = "one" };
        await using var session = theStore.LightweightSession();
        session.Store(note);
        await session.SaveChangesAsync();
        return note;
    }

    private async Task<long> RevisionOfAsync(Guid id)
    {
        await using var query = theStore.QuerySession();
        var metadata = await query.MetadataForAsync(await query.LoadAsync<RevisionedNote>(id));
        return metadata.ShouldNotBeNull().CurrentRevision;
    }

    [Fact]
    public async Task undo_delete_where_before_a_revisioned_upsert_of_the_same_type()
    {
        var note = await StoredNoteAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.UndoDeleteWhere<RevisionedNote>(x => x.Text == "matches nothing");
            note.Text = "two";
            session.UpdateRevision(note, 2);
            await session.SaveChangesAsync();
        }

        (await RevisionOfAsync(note.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task hard_delete_where_before_a_revisioned_upsert_of_the_same_type()
    {
        var note = await StoredNoteAsync();

        await using (var session = theStore.LightweightSession())
        {
            session.HardDeleteWhere<RevisionedNote>(x => x.Text == "matches nothing");
            note.Text = "two";
            session.UpdateRevision(note, 2);
            await session.SaveChangesAsync();
        }

        (await RevisionOfAsync(note.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task a_genuinely_stale_revision_is_still_refused_after_the_statement()
    {
        // The fix must not turn the guard off: a revision at or below the stored one is still a conflict
        var note = await StoredNoteAsync();

        await using var session = theStore.LightweightSession();
        session.UndoDeleteWhere<RevisionedNote>(x => x.Text == "matches nothing");
        session.UpdateRevision(note, 1);

        await Should.ThrowAsync<JasperFx.ConcurrencyException>(() => session.SaveChangesAsync());
    }
}

public class RevisionedNote
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
}
