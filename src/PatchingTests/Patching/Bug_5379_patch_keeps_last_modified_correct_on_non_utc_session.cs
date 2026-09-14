using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.LastModified;
using Marten.Patching;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace PatchingTests.Patching;

/// <summary>
/// #5379: a patch used to re-stamp <c>mt_last_modified</c> with <c>now() at time zone 'utc'</c>.
/// That expression yields a naive timestamp holding UTC wall-clock time, and assigning a naive
/// timestamp to a <c>timestamptz</c> column re-interprets it in the session's <c>TimeZone</c>, so
/// on any non-UTC database the stored instant was off by the UTC offset. The insert default
/// (<c>transaction_timestamp()</c>) was always right, so a row was stamped correctly on insert and
/// jumped on its first patch, which broke <c>ModifiedSince</c> / <c>ModifiedBefore</c> queries.
/// </summary>
/// <remarks>
/// The dev container and CI both run Postgres at <c>Etc/UTC</c>, where the buggy and correct forms
/// are byte-identical, so the session time zone is set on the connection string instead. Both
/// directions are covered: east of UTC the skew read in the past, west of UTC in the future.
/// </remarks>
public class Bug_5379_patch_keeps_last_modified_correct_on_non_utc_session: OneOffConfigurationsContext
{
    [Theory]
    [InlineData("Europe/Berlin")]
    [InlineData("America/Chicago")]
    public async Task patch_stamps_last_modified_with_the_actual_instant(string timeZone)
    {
        StoreOptions(opts =>
        {
            var connectionString = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
            {
                Timezone = timeZone
            }.ConnectionString;

            opts.Connection(connectionString);
        });

        var target = new Target { Number = 1 };
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        // Stamped by the column default, so this one is right and is a floor for the patch
        var inserted = (await theSession.MetadataForAsync(target)).LastModified;

        theSession.Patch<Target>(target.Id).Set(x => x.Number, 2);
        await theSession.SaveChangesAsync();
        var patchedBy = DateTimeOffset.UtcNow.AddSeconds(5);

        var patched = (await theSession.MetadataForAsync(target)).LastModified;

        patched.ShouldBeGreaterThanOrEqualTo(inserted);
        patched.ShouldBeLessThanOrEqualTo(patchedBy);

        // The reporter's symptom: the patched document must show up as modified since it was inserted
        var modified = await theSession.Query<Target>()
            .Where(x => x.ModifiedSince(inserted))
            .Select(x => x.Id)
            .ToListAsync();

        modified.ShouldContain(target.Id);
    }
}
