using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Metadata;
using Marten.Schema;
using Marten.Internal.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M1: end-to-end validation that the closed-shape document
/// storage handles the default-on metadata columns (mt_version,
/// mt_dotnet_type, mt_last_modified) without runtime Roslyn codegen.
/// Builds on closed_shape_storage_spike_tests — those exercise the
/// minimal (id, data) shape with metadata disabled; these exercise the
/// full default Marten shape with metadata on.
/// </summary>
public class closed_shape_storage_metadata_tests: BugIntegrationContext
{
    [Fact]
    public async Task store_and_load_with_default_metadata_columns_enabled()
    {
        // Default StoreOptions — Version + DotNetType + LastModified are on.
        theStore.UseLightweightSequentialGuidClosedShape<MetadataDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new MetadataDoc { Id = id, Name = "with-metadata" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<MetadataDoc>(id);

        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(id);
        loaded.Name.ShouldBe("with-metadata");
    }

    [Fact]
    public async Task version_member_is_populated_after_load()
    {
        theStore.UseLightweightSequentialGuidClosedShape<VersionedDoc>();

        var id = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Store(new VersionedDoc { Id = id, Name = "v1" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<VersionedDoc>(id);

        loaded.ShouldNotBeNull();
        // DocumentVersionBinder.BindParameter wrote a new CombGuid to
        // the row; the binder's Apply on load projects it onto Version.
        loaded.Version.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task version_changes_on_each_write()
    {
        theStore.UseLightweightSequentialGuidClosedShape<VersionedDoc>();

        var id = Guid.NewGuid();

        // [Version] turns on optimistic concurrency — second-and-later
        // writes must come after a Load so the session knows the
        // expected version. Without that the WHERE filter on
        // mt_version rejects the update.
        Guid firstVersion;
        await using (var session = theStore.LightweightSession())
        {
            var doc = new VersionedDoc { Id = id, Name = "v1" };
            session.Store(doc);
            await session.SaveChangesAsync();
            firstVersion = doc.Version;
        }
        firstVersion.ShouldNotBe(Guid.Empty);

        Guid secondVersion;
        await using (var session = theStore.LightweightSession())
        {
            var doc = await session.LoadAsync<VersionedDoc>(id);
            doc.ShouldNotBeNull();
            doc.Name = "v2";
            session.Store(doc);
            await session.SaveChangesAsync();
            secondVersion = doc.Version;
        }
        secondVersion.ShouldNotBe(Guid.Empty);
        secondVersion.ShouldNotBe(firstVersion);

        // Load round-trip — should reflect the latest version written.
        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<VersionedDoc>(id);
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("v2");
        loaded.Version.ShouldBe(secondVersion);
    }

    [Fact]
    public async Task last_modified_server_side_timestamp_is_written_and_round_trips()
    {
        theStore.UseLightweightSequentialGuidClosedShape<TimestampedDoc>();

        var id = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new TimestampedDoc { Id = id, Name = "ts" });
            await session.SaveChangesAsync();
        }

        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<TimestampedDoc>(id);

        loaded.ShouldNotBeNull();
        loaded.LastModified.ShouldBeGreaterThan(before);
        loaded.LastModified.ShouldBeLessThan(after);
    }

    [Fact]
    public async Task linq_query_works_with_metadata_columns_present()
    {
        // Sanity check: metadata columns don't disturb the LINQ path —
        // the selector reads `data` from its descriptor-configured index
        // regardless of how many metadata columns trail it.
        theStore.UseLightweightSequentialGuidClosedShape<MetadataDoc>();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new MetadataDoc { Id = Guid.NewGuid(), Name = "match" });
            session.Store(new MetadataDoc { Id = Guid.NewGuid(), Name = "miss" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var matches = await query.Query<MetadataDoc>()
            .Where(x => x.Name == "match")
            .ToListAsync();

        matches.Count.ShouldBe(1);
        matches[0].Name.ShouldBe("match");
    }
}

public class MetadataDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A document with a <c>[Version]</c>-annotated member. The
/// <see cref="DocumentVersionBinder{TDoc}"/> projects the stored
/// <c>mt_version</c> Guid onto this member via FEC-compiled setter.
/// </summary>
public class VersionedDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [Version]
    public Guid Version { get; set; }
}

/// <summary>
/// A document with a <c>[LastModified]</c>-annotated member.
/// <see cref="DocumentLastModifiedBinder{TDoc}"/> writes the server-side
/// timestamp back onto this member on read.
/// </summary>
public class TimestampedDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [LastModifiedMetadata]
    public DateTimeOffset LastModified { get; set; }
}
