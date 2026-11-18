#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Marten;
using Marten.Events;
using Marten.Events.Operations;
using Marten.Internal;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
///     #6049: stream compaction and the tombstone both delete the events' <c>mt_event_tag_*</c> rows ahead of the
///     events, and both build those deletes with <see cref="EventTagRowDeletes"/>, so a change to the tag tables
///     can't reach one and miss the other. This pins that the two statements carry the same delete per tag
///     table: same table, same alias, same tenant filter. Only the choice of events differs.
/// </summary>
public class Bug_6049_tag_row_deletes_are_shared: OneOffConfigurationsContext
{
    private void configure(TenancyStyle tenancy, DcbStorageMode mode = DcbStorageMode.TagTables)
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = tenancy;
            opts.Events.DcbStorageMode = mode;
            opts.Events.AddEventType<Bug6035Happened>();
            opts.Events.RegisterTagType<Bug6035TagId>("shared_course");
            opts.Events.RegisterTagType<Bug6035OtherTag>("shared_email");
        });
    }

    private string sqlOf(Func<IMartenSession, Weasel.Storage.IStorageOperation> operation)
    {
        using var session = theStore.LightweightSession("red");
        var martenSession = (IMartenSession)session;
        var builder = new BatchBuilder();
        operation(martenSession).ConfigureCommand(builder, martenSession);
        return string.Join("\n", builder.Compile().BatchCommands.Cast<Npgsql.NpgsqlBatchCommand>().Select(x => x.CommandText));
    }

    [Theory]
    [InlineData(TenancyStyle.Single)]
    [InlineData(TenancyStyle.Conjoined)]
    public void compaction_and_the_tombstone_delete_the_same_tag_rows(TenancyStyle tenancy)
    {
        configure(tenancy);

        var compaction = sqlOf(_ => new DeleteEventsOperation([1L, 2L]));
        var tombstone = sqlOf(_ => new DeleteStreamOperation(theStore.Options.EventGraph, Guid.NewGuid(), null));

        var suffixes = new[] { "shared_course", "shared_email" };
        for (var i = 0; i < suffixes.Length; i++)
        {
            // Everything but the events each one picks, which is the one part they choose for themselves
            var delete = Regex.Escape($"tag{i} as (delete from {SchemaName}.mt_event_tag_{suffixes[i]} t where t.seq_id ")
                         + (tenancy == TenancyStyle.Conjoined
                             ? @"[^\n]*? and t\.tenant_id = \$\d+\)"
                             : @"[^\n]*?\)");

            Regex.IsMatch(compaction, delete).ShouldBeTrue(compaction);
            Regex.IsMatch(tombstone, delete).ShouldBeTrue(tombstone);
        }

        if (tenancy == TenancyStyle.Single)
        {
            compaction.ShouldNotContain("t.tenant_id");
            tombstone.ShouldNotContain("t.tenant_id");
        }
    }

    [Fact]
    public void neither_deletes_tag_rows_under_hstore()
    {
        configure(TenancyStyle.Single, DcbStorageMode.HStore);

        sqlOf(_ => new DeleteEventsOperation([1L])).ShouldNotContain("mt_event_tag_");
        sqlOf(_ => new DeleteStreamOperation(theStore.Options.EventGraph, Guid.NewGuid(), null))
            .ShouldNotContain("mt_event_tag_");
    }
}
