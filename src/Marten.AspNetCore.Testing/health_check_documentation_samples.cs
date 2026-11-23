using System;
using System.Collections.Generic;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.AspNetCore.Testing;

public interface IInvoicingStore: IDocumentStore;

/// <summary>
///     The code samples in docs/events/projections/healthchecks.md that no other test shows. Compiled only, never
///     run.
/// </summary>
public static class health_check_documentation_samples
{
    // Whatever the application keeps up to date about the databases this node hosts
    public static readonly HashSet<string> LocallyOwnedDatabaseIdentifiers = new();

    public static void async_daemon_health_check()
    {
        var builder = WebApplication.CreateBuilder();

        #region sample_async_daemon_health_check

        // Add HealthCheck
        builder.Services.AddHealthChecks().AddMartenAsyncDaemonHealthCheck(maxEventLag: 500);

        // Map HealthCheck Endpoint
        var app = builder.Build();
        app.MapHealthChecks("/health");

        #endregion
    }

    public static void async_daemon_health_check_with_same_lag_time()
    {
        var builder = WebApplication.CreateBuilder();

        #region sample_async_daemon_health_check_with_same_lag_time

        // Add HealthCheck
        builder.Services.AddHealthChecks()
            .AddMartenAsyncDaemonHealthCheck(maxEventLag: 500, maxSameLagTime: TimeSpan.FromSeconds(30));

        // Map HealthCheck Endpoint
        var app = builder.Build();
        app.MapHealthChecks("/health");

        #endregion
    }

    public static void high_water_health_check()
    {
        var builder = WebApplication.CreateBuilder();

        #region sample_high_water_health_check

        // Add HealthCheck
        builder.Services.AddHealthChecks().AddMartenHighWaterHealthCheck(staleThreshold: TimeSpan.FromSeconds(30));

        // Map HealthCheck Endpoint
        var app = builder.Build();
        app.MapHealthChecks("/health");

        #endregion
    }

    public static void high_water_health_check_for_a_separate_store()
    {
        var builder = WebApplication.CreateBuilder();

        #region sample_high_water_health_check_for_a_separate_store

        builder.Services.AddMartenStore<IInvoicingStore>(opts =>
        {
            // ...
        }).AddAsyncDaemon(DaemonMode.Solo);

        builder.Services.AddHealthChecks()
            .AddMartenHighWaterHealthCheck<IInvoicingStore>(
                staleThreshold: TimeSpan.FromSeconds(30),
                autoRestart: true);

        #endregion
    }

    public static void high_water_health_check_for_locally_owned_databases()
    {
        var builder = WebApplication.CreateBuilder();

        #region sample_high_water_health_check_for_locally_owned_databases

        builder.Services.AddHealthChecks().AddMartenHighWaterHealthCheck(
            staleThreshold: TimeSpan.FromSeconds(30),

            // Only probe (and, with autoRestart, only restart) the databases this node owns. The
            // predicate receives each IMartenDatabase; return true for the ones the local node hosts.
            databaseFilter: db => LocallyOwnedDatabaseIdentifiers.Contains(db.Identifier),

            // Assert even under DaemonMode.ExternallyManaged (Wolverine-managed distribution). In this
            // mode only the per-tenant poll-cycle signal is used (never the store-global sequence-gap
            // heuristic), because an external owner can legitimately pause the mark — so a non-partitioned
            // store has nothing to assert on there.
            includeExternallyManaged: true);

        #endregion
    }
}
