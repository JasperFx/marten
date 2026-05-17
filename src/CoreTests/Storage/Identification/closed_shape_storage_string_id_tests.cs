using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Storage.Identification.ClosedShape;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Storage.Identification;

/// <summary>
/// W3 spike M4: validates that the closed-shape storage works for
/// string-key documents (externally-assigned identity), not just Guid.
/// Proves the closed-shape DocumentStorage classes are properly generic
/// over <c>TId</c> — the same descriptor + binder + operation shape
/// drives Guid-id and string-id documents identically.
/// </summary>
public class closed_shape_storage_string_id_tests: BugIntegrationContext
{
    [Fact]
    public async Task store_and_load_string_keyed_document()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
            opts.Schema.For<StringKeyedDoc>().Identity(x => x.Id);
        });

        theStore.UseExternallyAssignedStringClosedShape<StringKeyedDoc>();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new StringKeyedDoc { Id = "user-42", Name = "alpha" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var loaded = await query.LoadAsync<StringKeyedDoc>("user-42");
        loaded.ShouldNotBeNull();
        loaded.Name.ShouldBe("alpha");
    }

    [Fact]
    public async Task linq_query_works_on_string_keyed_document()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
            opts.Schema.For<StringKeyedDoc>().Identity(x => x.Id);
        });

        theStore.UseExternallyAssignedStringClosedShape<StringKeyedDoc>();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new StringKeyedDoc { Id = "a", Name = "match" });
            session.Store(new StringKeyedDoc { Id = "b", Name = "miss" });
            await session.SaveChangesAsync();
        }

        await using var query = theStore.QuerySession();
        var matches = await query.Query<StringKeyedDoc>()
            .Where(x => x.Name == "match")
            .ToListAsync();

        matches.Count.ShouldBe(1);
        matches[0].Id.ShouldBe("a");
    }

    [Fact]
    public async Task store_throws_when_id_is_null_or_empty()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
            opts.Schema.For<StringKeyedDoc>().Identity(x => x.Id);
        });

        theStore.UseExternallyAssignedStringClosedShape<StringKeyedDoc>();

        // StringIdentification.AssignIfMissing throws when the document's
        // id is null/empty — externally-assigned strategy refuses to
        // auto-generate.
        await using var session = theStore.LightweightSession();
        Should.Throw<InvalidOperationException>(() =>
        {
            session.Store(new StringKeyedDoc { Id = string.Empty, Name = "no-id" });
        });
    }

    [Fact]
    public async Task identity_session_returns_same_instance_for_string_keys()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
            opts.Schema.For<StringKeyedDoc>().Identity(x => x.Id);
        });

        theStore.UseExternallyAssignedStringClosedShape<StringKeyedDoc>();

        await using (var session = theStore.LightweightSession())
        {
            session.Store(new StringKeyedDoc { Id = "shared", Name = "value" });
            await session.SaveChangesAsync();
        }

        // The identity-map selector is generic over TId — the closed
        // type for this doc is <StringKeyedDoc, string>. Identity map
        // dictionary is Dictionary<string, StringKeyedDoc>.
        await using var session2 = theStore.IdentitySession();
        var first = await session2.LoadAsync<StringKeyedDoc>("shared");
        var second = await session2.LoadAsync<StringKeyedDoc>("shared");

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public async Task insert_throws_on_collision_for_string_keys()
    {
        StoreOptions(opts =>
        {
            opts.Policies.DisableInformationalFields();
            opts.Schema.For<StringKeyedDoc>().Identity(x => x.Id);
        });

        theStore.UseExternallyAssignedStringClosedShape<StringKeyedDoc>();

        await using (var session = theStore.LightweightSession())
        {
            session.Insert(new StringKeyedDoc { Id = "dup", Name = "first" });
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Insert(new StringKeyedDoc { Id = "dup", Name = "second" });
            await Should.ThrowAsync<DocumentAlreadyExistsException>(
                () => session.SaveChangesAsync());
        }
    }
}

public class StringKeyedDoc
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
