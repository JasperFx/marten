#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record LibraryLoanId(Guid Value);

public record LibraryBookReturned(int DaysLate);
public record LibraryReminderSent(DateTimeOffset SentAt);
public record LibraryLoanExtended(int Days);

[BoundaryAggregate]
public class LibraryLoanState
{
    public bool Returned { get; set; }
    public int Reminders { get; set; }

    public void Apply(LibraryBookReturned _) => Returned = true;
    public void Apply(LibraryReminderSent _) => Reminders++;
}

// #5887: a DCB query condition can name event types (AndEventsOfType), and the specification's
// append condition only fails on a later event the query MATCHES -- type included. Marten keeps its
// conflict versions per tag value, so by default an event of an unselected type carrying the same
// tag value still conflicts. DcbConflictScope.TagValueAndEventType keeps a version per
// (tag value, event type) as well and checks the one the condition names.
//
// The store-agnostic facts that used to live here run for every Critter Stack store in
// JasperFx.Events.ComplianceTests' DcbAppendConditionCompliance (dcb_append_condition_compliance).
// What stays is Marten's own: the TagValue and TagValueRecordingEventTypes scopes, HStore mode and the rollout.
[Collection("OneOffs")]
public class dcb_conflict_scope_by_event_type: OneOffConfigurationsContext
{
    private void Configure(StoreOptions opts, DcbConflictScope scope, DcbStorageMode mode = DcbStorageMode.TagTables)
    {
        opts.Events.AddEventType<LibraryBookReturned>();
        opts.Events.AddEventType<LibraryReminderSent>();
        opts.Events.AddEventType<LibraryLoanExtended>();

        opts.Events.RegisterTagType<LibraryLoanId>("library_loan");
        opts.Events.DcbStorageMode = mode;
        opts.Events.DcbConflictScope = scope;

        // No write retries, so a deadlock (40P01) fails the test instead of committing on a retry
        opts.ConfigureWritePolly(_ => { });
    }

    private DocumentStore UseScope(DcbConflictScope scope, DcbStorageMode mode = DcbStorageMode.TagTables)
        => StoreOptions(opts => Configure(opts, scope, mode));

    private static EventTagQuery ReturnsOf(LibraryLoanId loan)
        => new EventTagQuery().Or(loan).AndEventsOfType<LibraryBookReturned>();

    private static EventTagQuery RemindersOf(LibraryLoanId loan)
        => new EventTagQuery().Or(loan).AndEventsOfType<LibraryReminderSent>();

    private static IEvent Tagged<T>(IDocumentSession session, T data, LibraryLoanId loan) where T : notnull
    {
        var e = session.Events.BuildEvent(data);
        e.WithTag(loan);
        return e;
    }

    private static async Task AppendElsewhere<T>(IDocumentStore store, T data, LibraryLoanId loan) where T : notnull
    {
        await using var session = store.LightweightSession();
        session.Events.Append(Guid.NewGuid(), Tagged(session, data, loan));
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task documentation_sample()
    {
        var loanId = Guid.NewGuid();

        #region sample_marten_dcb_conflict_scope
        var store = StoreOptions(opts =>
        {
            opts.Events.RegisterTagType<LibraryLoanId>("library_loan");

            // Honour AndEventsOfType in the DCB conflict check, as the DCB specification does.
            // On a running system, deploy TagValueRecordingEventTypes to every node first.
            opts.Events.DcbConflictScope = DcbConflictScope.TagValueAndEventType;
        });

        await using var session = store.LightweightSession();

        // The decision only folds returns...
        var query = new EventTagQuery()
            .Or(new LibraryLoanId(loanId))
            .AndEventsOfType<LibraryBookReturned>();

        var boundary = await session.Events.FetchForWritingByTags<LibraryLoanState>(query);

        // ...so a reminder for the same loan appended in the meantime by another session
        // no longer fails this save with a DcbConcurrencyException
        #endregion

        await AppendElsewhere(store, new LibraryReminderSent(DateTimeOffset.UtcNow), new LibraryLoanId(loanId));
        boundary.AppendOne(Tagged(session, new LibraryBookReturned(0), new LibraryLoanId(loanId)));
        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }

    // The issue's own repro: a reminder (a type the decision does not fold) lands between the read
    // and the save of a decision that only selects returns.
    [Theory]
    [InlineData(DcbConflictScope.TagValue, DcbStorageMode.TagTables, true)]
    [InlineData(DcbConflictScope.TagValueRecordingEventTypes, DcbStorageMode.TagTables, true)]
    [InlineData(DcbConflictScope.TagValueAndEventType, DcbStorageMode.HStore, false)]
    public async Task an_event_of_a_type_the_condition_does_not_name(DcbConflictScope scope, DcbStorageMode mode,
        bool conflicts)
    {
        var store = UseScope(scope, mode);
        var loan = new LibraryLoanId(Guid.NewGuid());

        await using var session = store.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<LibraryLoanState>(ReturnsOf(loan));

        await AppendElsewhere(store, new LibraryReminderSent(DateTimeOffset.UtcNow), loan);

        boundary.AppendOne(Tagged(session, new LibraryBookReturned(0), loan));

        if (conflicts)
        {
            await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
        }
        else
        {
            await Should.NotThrowAsync(() => session.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData(DcbStorageMode.HStore)]
    public async Task an_event_of_a_type_the_condition_names_still_conflicts(DcbStorageMode mode)
    {
        var store = UseScope(DcbConflictScope.TagValueAndEventType, mode);
        var loan = new LibraryLoanId(Guid.NewGuid());

        await using var session = store.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<LibraryLoanState>(ReturnsOf(loan));

        await AppendElsewhere(store, new LibraryBookReturned(3), loan);

        boundary.AppendOne(Tagged(session, new LibraryBookReturned(0), loan));
        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }

    // The rollout: every node first records the per-type versions, then nodes switch to checking
    // them. A node that has switched sees a conflicting append from a node still on the step.
    [Fact]
    public async Task a_node_checking_event_types_sees_appends_from_a_node_recording_them()
    {
        var checking = UseScope(DcbConflictScope.TagValueAndEventType);
        await checking.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var recording = SeparateStore(opts => Configure(opts, DcbConflictScope.TagValueRecordingEventTypes));
        var loan = new LibraryLoanId(Guid.NewGuid());

        await using var session = checking.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<LibraryLoanState>(ReturnsOf(loan));

        await AppendElsewhere(recording, new LibraryBookReturned(3), loan);

        boundary.AppendOne(Tagged(session, new LibraryBookReturned(0), loan));
        await Should.ThrowAsync<DcbConcurrencyException>(() => session.SaveChangesAsync());
    }

    // ...and why the step exists. This pins the hazard the docs warn about rather than a behaviour
    // anyone should rely on: a node still on the default does not maintain the per-type versions,
    // so its conflicting append is invisible to a node checking them.
    [Fact]
    public async Task skipping_the_rollout_step_misses_a_conflict()
    {
        var checking = UseScope(DcbConflictScope.TagValueAndEventType);
        await checking.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        var old = SeparateStore(opts => Configure(opts, DcbConflictScope.TagValue));
        var loan = new LibraryLoanId(Guid.NewGuid());

        await using var session = checking.LightweightSession();
        var boundary = await session.Events.FetchForWritingByTags<LibraryLoanState>(ReturnsOf(loan));

        await AppendElsewhere(old, new LibraryBookReturned(3), loan);

        boundary.AppendOne(Tagged(session, new LibraryBookReturned(0), loan));
        await Should.NotThrowAsync(() => session.SaveChangesAsync());
    }
}
