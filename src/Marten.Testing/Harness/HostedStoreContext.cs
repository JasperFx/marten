#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Events.Daemon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// Base for tests that need a real <see cref="IHost"/> with AddMarten —
    /// daemon coordination, DI-registered projections, ancillary stores —
    /// rather than a bare DocumentStore. Owns host lifetime: every host
    /// started through <see cref="StartHostAsync"/> is stopped and disposed
    /// (newest first) when the test class completes.
    /// </summary>
    /// <remarks>
    /// The main store gets this class's <see cref="SchemaName"/> (derived from
    /// the test class name, like OneOffConfigurationsContext). Tests adding
    /// secondary stores via <c>AddMartenStore&lt;T&gt;</c> in the
    /// <c>configureServices</c> callback should derive their schema from
    /// <see cref="SchemaName"/> too (e.g. <c>$"{SchemaName}_products"</c>) so
    /// the whole class stays isolated under one prefix.
    /// </remarks>
    public abstract class HostedStoreContext: IAsyncLifetime
    {
        private readonly List<IHost> _hosts = new();

        protected HostedStoreContext()
        {
            SchemaName = GetType().Name.ToLower().Sanitize();
        }

        protected string SchemaName { get; }

        /// <summary>
        /// Start (and track) a host whose main Marten store is pre-configured
        /// with the test connection string, <see cref="SchemaName"/>, and
        /// Npgsql logging disabled. <paramref name="daemonMode"/> maps to
        /// AddAsyncDaemon; <paramref name="configureMarten"/> receives the
        /// AddMarten fluent expression for chained calls like
        /// ApplyAllDatabaseChangesOnStartup; <paramref name="configureServices"/>
        /// runs after AddMarten for extra registrations (ancillary stores,
        /// logging...).
        /// </summary>
        protected async Task<IHost> StartHostAsync(
            Action<StoreOptions> configure,
            DaemonMode? daemonMode = null,
            Action<IServiceCollection>? configureServices = null,
            Action<MartenServiceCollectionExtensions.MartenConfigurationExpression>? configureMarten = null)
        {
            var host = await Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    var marten = services.AddMarten(opts =>
                    {
                        opts.Connection(ConnectionSource.ConnectionString);
                        opts.DisableNpgsqlLogging = true;
                        opts.DatabaseSchemaName = SchemaName;

                        configure(opts);
                    });

                    if (daemonMode.HasValue)
                    {
                        marten.AddAsyncDaemon(daemonMode.Value);
                    }

                    configureMarten?.Invoke(marten);

                    configureServices?.Invoke(services);
                }).StartAsync();

            _hosts.Add(host);

            return host;
        }

        protected static DocumentStore StoreOf(IHost host)
        {
            return (DocumentStore)host.Services.GetRequiredService<IDocumentStore>();
        }

        public virtual ValueTask InitializeAsync()
        {
            return default;
        }

        public virtual async ValueTask DisposeAsync()
        {
            for (var i = _hosts.Count - 1; i >= 0; i--)
            {
                var host = _hosts[i];
                try
                {
                    await host.StopAsync(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    host.Dispose();
                }
            }

            _hosts.Clear();
        }
    }
}
