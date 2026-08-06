using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// Regression guard for https://github.com/JasperFx/marten/issues/5192, fixed in the bundled
// JasperFx.Events.SourceGenerator by jasperfx#637.
//
// The generator registers an EventProjection's discovered published document types -- the ones it sees
// flowing through ops.Store/Insert/Update inside an ApplyAsync override (marten#4166). It used to do that
// by emitting a parameterless constructor into the user's partial class, which failed two ways for exactly
// the projections that need dependencies injected, i.e. the ones registered through AddProjectionWithServices:
//
//   1. It does not compile at all against a primary constructor. C# requires every other constructor to
//      chain through the primary one, so the generated file broke the whole build with CS8862. That
//      half of this test is the mere existence of DependencyAwareProjection below -- if the generator
//      regresses, this project stops compiling.
//
//   2. Where it did compile -- an ordinary dependency-taking constructor -- the container calls THAT
//      constructor, so the generated parameterless one never ran and the published types went silently
//      unregistered. Only PublishedTypes() can see that, which is what the test asserts.
//
// Both shapes are covered because they hit different generator paths, and shape 2 is the one that fails
// quietly rather than loudly.
// (partial because the generator wraps a nested projection's emitted members in its containing types.)
public partial class Bug_5192_event_projection_with_injected_dependencies
{
    public record ThingCreated(Guid Id, string Name);

    public class Thing
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class OtherThing
    {
        public Guid Id { get; set; }
    }

    // Shape 1: a primary constructor. Before the fix the generated partial did not compile.
    public partial class PrimaryConstructorProjection(ILogger<PrimaryConstructorProjection> logger): EventProjection
    {
        public override ValueTask ApplyAsync(IDocumentOperations operations, IEvent e,
            CancellationToken cancellation)
        {
            if (e.Data is ThingCreated created)
            {
                logger.LogDebug("Storing {Name}", created.Name);
                operations.Store(new Thing { Id = created.Id, Name = created.Name });
            }

            return new ValueTask();
        }
    }

    // Shape 2: an ordinary injected constructor. Before the fix this compiled and registered nothing.
    public partial class DependencyAwareProjection: EventProjection
    {
        private readonly ILogger<DependencyAwareProjection> _logger;

        public DependencyAwareProjection(ILogger<DependencyAwareProjection> logger) => _logger = logger;

        public override ValueTask ApplyAsync(IDocumentOperations operations, IEvent e,
            CancellationToken cancellation)
        {
            if (e.Data is ThingCreated created)
            {
                _logger.LogDebug("Storing {Name}", created.Name);
                operations.Store(new Thing { Id = created.Id, Name = created.Name });
                operations.Store(new OtherThing { Id = created.Id });
            }

            return new ValueTask();
        }
    }

    [Fact]
    public void published_types_are_registered_on_a_projection_with_a_primary_constructor()
    {
        new PrimaryConstructorProjection(NullLogger<PrimaryConstructorProjection>.Instance)
            .PublishedTypes()
            .ShouldContain(typeof(Thing));
    }

    [Fact]
    public void published_types_are_registered_on_a_container_built_projection()
    {
        // The instance is built the way AddProjectionWithServices builds it -- through the constructor
        // that takes the dependency, never through a parameterless one.
        var publishedTypes = new DependencyAwareProjection(NullLogger<DependencyAwareProjection>.Instance)
            .PublishedTypes()
            .ToArray();

        publishedTypes.ShouldContain(typeof(Thing));
        publishedTypes.ShouldContain(typeof(OtherThing));
    }

    [Fact]
    public async Task the_store_provisions_storage_for_the_discovered_published_types()
    {
        // The point of registering published types: a projection's document storage is known ahead of
        // time rather than only provisioned on demand. Nothing here constructs the projection by hand.
        await using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "bug_5192";
            opts.Projections.Add(
                new DependencyAwareProjection(NullLogger<DependencyAwareProjection>.Instance),
                ProjectionLifecycle.Inline);
        });

        store.Options.Projections.AllPublishedTypes().ShouldContain(typeof(Thing));
        store.Options.Projections.AllPublishedTypes().ShouldContain(typeof(OtherThing));
    }
}
