using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Bugs.AliasCollisionAlpha;
using EventSourcingTests.Bugs.AliasCollisionBeta;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs
{
    /// <summary>
    /// Marten derives an event's <c>mt_events.type</c> alias from the CLR type's simple name only, so two event
    /// types that share a name but live in different namespaces collide on one alias. <c>mt_events.mt_dotnet_type</c>
    /// exists to disambiguate them on read, and <c>EventDocumentStorage.ResolveAsync</c> does consult it — but only
    /// via <c>EventGraph.TryGetRegisteredMappingForDotNetTypeName</c>, which is a linear scan over mappings that are
    /// <em>already registered</em>. It never falls back to <c>TypeForDotNetName</c>.
    ///
    /// <para>
    /// Nothing registers event types up front — not even projections — so the graph is populated lazily by whichever
    /// code path touches a type first (an append, or a read whose alias lookup misses). A process that has only ever
    /// touched one side of a collision therefore silently deserializes the other side's rows into the wrong CLR type.
    /// It is silent whenever the two payloads are JSON-compatible, which same-named events in one domain usually are.
    /// </para>
    ///
    /// <para>
    /// Because registration is per-process and driven by runtime ordering, the same stored event resolves correctly
    /// on one node and incorrectly on another, and a node self-heals as soon as it happens to append the missing
    /// type. Reads never register it, so a read-only node can stay wrong indefinitely.
    /// </para>
    /// </summary>
    public class Bug_XXXX_dotnet_type_disambiguation_requires_registered_mapping: BugIntegrationContext
    {
        [Fact]
        public void dotnet_type_lookup_finds_a_type_that_has_not_been_registered_yet()
        {
            var options = new StoreOptions();
            options.Events.AddEventType<AliasCollisionAlpha.ThingHappened>();

            var beta = typeof(AliasCollisionBeta.ThingHappened);
            var dotNetTypeName = $"{beta.FullName}, {beta.Assembly.GetName().Name}";

            // This is what EventDocumentStorage.ResolveAsync leans on to correct a colliding alias. It is a scan
            // over already-registered mappings, so an unregistered type yields null, the wrong mapping stands, and
            // the bad deserialization below happens with no exception and nothing in the logs.
            var mapping = options.EventGraph.TryGetRegisteredMappingForDotNetTypeName(dotNetTypeName);

            mapping.ShouldNotBeNull();
            mapping.DocumentType.ShouldBe(beta);
        }

        [Fact]
        public async Task resolves_by_dotnet_type_when_the_colliding_type_is_not_registered()
        {
            // Both types are known here, so this store can append either one.
            var writeStore = SeparateStore(opts =>
            {
                opts.Events.AddEventType<AliasCollisionAlpha.ThingHappened>();
                opts.Events.AddEventType<AliasCollisionBeta.ThingHappened>();
            });

            await writeStore.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

            writeStore.Events.EventMappingFor<AliasCollisionAlpha.ThingHappened>().Alias
                .ShouldBe(writeStore.Events.EventMappingFor<AliasCollisionBeta.ThingHappened>().Alias);

            var streamId = Guid.NewGuid();
            await using (var session = writeStore.LightweightSession())
            {
                session.Events.StartStream(streamId, new AliasCollisionBeta.ThingHappened(streamId, "beta", 42));
                await session.SaveChangesAsync();
            }

            // A second store standing in for a process that has only ever touched the Alpha side — say a node whose
            // projections reference Alpha and which has not handled a command that appends Beta. The alias resolves
            // to Alpha, and the mt_dotnet_type correction finds nothing to swap to because Beta was never registered.
            var readStore = SeparateStore(opts => opts.Events.AddEventType<AliasCollisionAlpha.ThingHappened>());

            await using var query = readStore.QuerySession();
            var events = await query.Events.FetchStreamAsync(streamId);

            // Pre-fix this is an AliasCollisionAlpha.ThingHappened: the Beta payload deserializes into it cleanly,
            // dropping Count, with no exception and nothing in the logs.
            events.Single().Data.ShouldBeOfType<AliasCollisionBeta.ThingHappened>()
                .Count.ShouldBe(42);
        }
    }
}

namespace EventSourcingTests.Bugs.AliasCollisionAlpha
{
    public record ThingHappened(Guid Id, string Name);
}

namespace EventSourcingTests.Bugs.AliasCollisionBeta
{
    // Deliberately JSON-compatible with the Alpha shape so a wrong-type read fails silently rather than throwing.
    public record ThingHappened(Guid Id, string Name, int Count);
}
