#nullable enable
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// #5622. <c>mt_dcb_tag_version</c> takes an UPDATE on every <c>FetchForWritingByTags</c> →
/// <c>SaveChangesAsync</c> and is built for PostgreSQL's HOT path: the only column that changes,
/// <c>version</c>, is unindexed so a bump rewrites no index entry. HOT also needs free space in the same
/// heap page, which is what a lower <c>fillfactor</c> reserves — available since Weasel 9.42.0.
///
/// <para>
/// It is <b>opt-in</b>, and the measurement is why. On PostgreSQL 17, 50k single-statement-per-transaction
/// bumps: with a SMALL tag set (50 rows, one heap page) the default already gives <b>100% HOT updates</b>
/// and fillfactor changes nothing; with a LARGE tag set (200k rows, a hot subset spread across packed
/// pages) the default gives <b>92.9% HOT</b> and the heap grows, while fillfactor 70 gives <b>100% HOT</b>
/// and no growth — at <b>45% more disk permanently</b> (19 MB against 13 MB for the same rows). Marten
/// cannot know a store's tag cardinality, so a default would be pure cost for the common case.
/// </para>
///
/// <para>
/// These tests pin both directions, because the default mattering is as much the point as the option
/// working: an unset option must leave the table exactly as every existing deployment has it, since a
/// declared storage parameter is visible to the schema delta.
/// </para>
/// </summary>
[Collection("OneOffs")]
public class dcb_tag_version_fillfactor: OneOffConfigurationsContext
{
    private async Task<string> reloptionsFor(int? fillFactor)
    {
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.DcbTagVersionFillFactor = fillFactor;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = theStore.QuerySession();
        var options = await session.AdvancedSql.QueryAsync<string>(
            $"select coalesce(array_to_string(reloptions, ','), '') from pg_class c "
            + $"join pg_namespace n on n.oid = c.relnamespace "
            + $"where c.relname = 'mt_dcb_tag_version' and n.nspname = '{theStore.Options.Events.DatabaseSchemaName}'",
            TestContext.Current.CancellationToken);

        return options.Count == 0 ? "(no table)" : options[0];
    }

    [Fact]
    public async Task unset_by_default_so_an_existing_deployment_sees_no_delta()
    {
        // The important half. A declared storage parameter would show up as a pending
        // ALTER TABLE … SET (fillfactor = N) for everyone on upgrade.
        (await reloptionsFor(null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task setting_it_reaches_the_table()
    {
        (await reloptionsFor(70)).ShouldContain("fillfactor=70");
    }

    [Fact]
    public async Task the_schema_is_clean_after_applying_it()
    {
        // Weasel compares only DECLARED storage parameters, so a table carrying fillfactor must not then
        // report a difference against the configuration that asked for it -- otherwise setting the option
        // would leave every deployment permanently "needs migration".
        StoreOptions(opts =>
        {
            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.RegisterTagType<StudentId>("student");
            opts.Events.DcbTagVersionFillFactor = 70;
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
