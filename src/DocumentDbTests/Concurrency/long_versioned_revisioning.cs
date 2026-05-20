using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Concurrency;

public class long_versioned_revisioning: OneOffConfigurationsContext
{
    [Fact]
    public void infer_numeric_revisioning_from_ILongVersioned()
    {
        var mapping = (DocumentMapping)theStore.Options.Storage.FindMapping(typeof(LongVersionedDoc));
        mapping.UseNumericRevisions.ShouldBeTrue();
        mapping.Metadata.Revision.Enabled.ShouldBeTrue();
        mapping.Metadata.Revision.Member.Name.ShouldBe("Version");
    }

    [Fact]
    public async Task round_trips_a_version_greater_than_int32()
    {
        // #4528: an ILongVersioned document carries the 64-bit revision (e.g. a
        // MultiStreamProjection's event sequence number). The bigint mt_version column
        // must round-trip a value > Int32.MaxValue without the truncation an int
        // IRevisioned member would suffer.
        var doc = new LongVersionedDoc { Id = Guid.NewGuid(), Name = "big" };
        var bigVersion = (long)int.MaxValue + 12345;

        theSession.UpdateRevision(doc, bigVersion);
        await theSession.SaveChangesAsync();

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<LongVersionedDoc>(doc.Id);
        loaded.ShouldNotBeNull();
        loaded.Version.ShouldBe(bigVersion);
    }
}

public class LongVersionedDoc: ILongVersioned
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Version { get; set; }
}
