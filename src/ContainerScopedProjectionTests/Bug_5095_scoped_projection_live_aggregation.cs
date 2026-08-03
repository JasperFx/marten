using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace ContainerScopedProjectionTests;

/// <summary>
/// Repro for https://github.com/JasperFx/marten/issues/5095.
///
/// A projection registered through <c>AddProjectionWithServices</c> with a Scoped or
/// Transient lifetime is wrapped in <c>ScopedAggregationWrapper</c>. That wrapper does
/// not implement <c>IAggregatorSource&lt;TQuerySession&gt;</c>, so
/// <c>ProjectionGraph.AggregatorFor&lt;T&gt;()</c> never finds it: the lookup falls
/// through <c>tryFindProjectionSourceForAggregateType</c> to the conventional
/// aggregation built off the aggregate type itself. Every native aggregation path --
/// <c>AggregateStreamAsync</c>, <c>RebuildSingleStreamAsync</c>, <c>AggregateToAsync</c>
/// -- therefore silently ignores the projection's own logic. Registering the same
/// projection as a Singleton exposes the real projection and everything works.
///
/// Separately, <c>ProjectionSourceWrapperBase</c>'s constructor copies name/version/
/// options off the resolved source but never calls <c>AssembleAndAssertValidity()</c>
/// on it, so an invalid projection that a Singleton registration rejects at startup is
/// silently accepted when registered Scoped.
///
/// The aggregates here deliberately have NO conventional Create/Apply methods, so the
/// conventional fallback cannot accidentally produce a correct-looking answer -- the
/// projection's Evolve override is the only thing that can build them.
/// </summary>
[Collection("ioc")]
public class Bug_5095_scoped_projection_live_aggregation
{
    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task live_aggregation_uses_the_registered_projection(ServiceLifetime lifetime)
    {
        var streamId = Guid.NewGuid();
        using var host = await startHost<TallyProjection>($"b5095_live_{suffix(lifetime)}", lifetime);
        var store = host.Services.GetRequiredService<IDocumentStore>();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Tally>(streamId, new Incremented(2), new Incremented(3));
            await session.SaveChangesAsync();
        }

        await using var query = store.QuerySession();
        var tally = await query.Events.AggregateStreamAsync<Tally>(streamId);

        tally.ShouldNotBeNull();
        // The projection multiplies by the injected factor of 10; conventional
        // aggregation off Tally would have no way to produce this (or anything).
        tally.Total.ShouldBe(50);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task rebuild_single_stream_uses_the_registered_projection(ServiceLifetime lifetime)
    {
        var streamId = Guid.NewGuid();
        using var host = await startHost<TallyProjection>($"b5095_rebuild_{suffix(lifetime)}", lifetime);
        var store = host.Services.GetRequiredService<IDocumentStore>();

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Tally>(streamId, new Incremented(2), new Incremented(3));
            await session.SaveChangesAsync();
        }

        await store.Advanced.RebuildSingleStreamAsync<Tally>(streamId);

        await using var query = store.QuerySession();
        var tally = await query.LoadAsync<Tally>(streamId);

        tally.ShouldNotBeNull();
        tally.Total.ShouldBe(50);
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task invalid_projection_is_rejected_at_configuration_regardless_of_lifetime(
        ServiceLifetime lifetime)
    {
        // InvalidTally has a public constructor taking an event -- a conventional
        // aggregate handler -- while InvalidTallyProjection overrides EvolveAsync.
        // Mixing the two is a configuration error Marten rejects for a Singleton.
        await Should.ThrowAsync<InvalidProjectionException>(async () =>
        {
            using var host = await startHost<InvalidTallyProjection>(
                $"b5095_invalid_{suffix(lifetime)}", lifetime);

            // Building the store is what runs validation
            var store = host.Services.GetRequiredService<IDocumentStore>();
            await using var session = store.LightweightSession();
            await session.SaveChangesAsync();
        });
    }

    private static string suffix(ServiceLifetime lifetime) => lifetime.ToString().ToLowerInvariant();

    private static async Task<IHost> startHost<TProjection>(string schema, ServiceLifetime lifetime)
        where TProjection : class, IMartenRegistrable
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
            await conn.CloseAsync();
        }

        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IMultiplier>(new Multiplier(10));

                services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DatabaseSchemaName = schema;
                    })
                    .AddProjectionWithServices<TProjection>(ProjectionLifecycle.Inline, lifetime);
            })
            .StartAsync();
    }
}

public interface IMultiplier
{
    int Factor { get; }
}

public class Multiplier: IMultiplier
{
    public Multiplier(int factor) => Factor = factor;
    public int Factor { get; }
}

public record Incremented(int Amount);

/// <summary>
/// Deliberately has no conventional Create/Apply methods -- only the projection can build it.
/// </summary>
public class Tally
{
    public Guid Id { get; set; }
    public int Total { get; set; }
}

public class TallyProjection: SingleStreamProjection<Tally, Guid>
{
    private readonly IMultiplier _multiplier;

    public TallyProjection(IMultiplier multiplier)
    {
        _multiplier = multiplier;
    }

    public override Tally? Evolve(Tally? snapshot, Guid id, IEvent e)
    {
        snapshot ??= new Tally { Id = id };

        if (e.Data is Incremented incremented)
        {
            snapshot.Total += incremented.Amount * _multiplier.Factor;
        }

        return snapshot;
    }
}

/// <summary>
/// Invalid on purpose: a public constructor taking an event is a conventional aggregate
/// handler, and the projection also overrides EvolveAsync.
/// </summary>
public class InvalidTally
{
    public InvalidTally()
    {
    }

    public InvalidTally(Incremented incremented)
    {
        Total = incremented.Amount;
    }

    public Guid Id { get; set; }
    public int Total { get; set; }
}

public class InvalidTallyProjection: SingleStreamProjection<InvalidTally, Guid>
{
    private readonly IMultiplier _multiplier;

    public InvalidTallyProjection(IMultiplier multiplier)
    {
        _multiplier = multiplier;
    }

    public override ValueTask<InvalidTally?> EvolveAsync(InvalidTally? snapshot, Guid id, IQuerySession session,
        IEvent e, System.Threading.CancellationToken cancellation)
    {
        snapshot ??= new InvalidTally { Id = id };

        if (e.Data is Incremented incremented)
        {
            snapshot.Total += incremented.Amount * _multiplier.Factor;
        }

        return new ValueTask<InvalidTally?>(snapshot);
    }
}
