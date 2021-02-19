using System;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marten
{
    public static class MartenServiceCollectionExtensions
    {
        /// <summary>
        /// Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
        /// to your application with the given Postgresql connection string and Marten
        /// defaults
        /// </summary>
        /// <param name="services"></param>
        /// <param name="connectionString">The connection string to your application's Postgresql database</param>
        /// <returns></returns>
        public static MartenConfigurationExpression AddMarten(this IServiceCollection services, string connectionString)
        {
            var options = new StoreOptions();
            options.Connection(connectionString);
            return services.AddMarten(options);
        }

        /// <summary>
        /// Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
        /// to your application using the configured StoreOptions
        /// </summary>
        /// <param name="services"></param>
        /// <param name="options">The Marten configuration for this application</param>
        /// <returns></returns>
        public static MartenConfigurationExpression AddMarten(this IServiceCollection services, StoreOptions options)
        {
            services.AddSingleton<IDocumentStore>(s =>
            {
                var logger = s.GetService<ILogger<IDocumentStore>>() ?? new NullLogger<IDocumentStore>();
                options.Logger(new DefaultMartenLogger(logger));

                return new DocumentStore(options);
            });

            // This can be overridden by the expression following
            services.AddSingleton<ISessionFactory, DefaultSessionFactory>();


            services.AddScoped(s => s.GetRequiredService<ISessionFactory>().QuerySession());
            services.AddScoped(s => s.GetRequiredService<ISessionFactory>().OpenSession());

            switch (options.Events.Daemon.Mode)
            {
                case DaemonMode.Disabled:
                    // Nothing
                    break;

                case DaemonMode.Solo:
                    services.AddSingleton<INodeCoordinator, SoloCoordinator>();
                    services.AddSingleton(x=>
                        x.GetRequiredService<IDocumentStore>()
                            .BuildProjectionDaemon(x.GetRequiredService<ILogger<IProjectionDaemon>>()));
                    services.AddHostedService<AsyncProjectionHostedService>();
                    break;

                case DaemonMode.HotCold:
                    services.AddSingleton<INodeCoordinator, HotColdCoordinator>();
                    services.AddSingleton(x =>
                        x.GetRequiredService<IDocumentStore>()
                            .BuildProjectionDaemon(x.GetRequiredService<ILogger<IProjectionDaemon>>()));
                    services.AddHostedService<AsyncProjectionHostedService>();
                    break;

            }

            return new MartenConfigurationExpression(services, options);
        }

        /// <summary>
        /// Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
        /// to your application using the configured StoreOptions
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static MartenConfigurationExpression AddMarten(this IServiceCollection services, Action<StoreOptions> configure)
        {
            var options = new StoreOptions();
            configure(options);

            return services.AddMarten(options);
        }

        public class MartenConfigurationExpression
        {
            private readonly IServiceCollection _services;
            private readonly StoreOptions _options;

            internal MartenConfigurationExpression(IServiceCollection services, StoreOptions options)
            {
                _services = services;
                _options = options;
            }

            /// <summary>
            /// Use an alternative strategy / configuration for opening IDocumentSession or IQuerySession
            /// objects in the application with a custom ISessionFactory type registered as a singleton
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <returns></returns>
            public MartenConfigurationExpression BuildSessionsWith<T>() where T : class, ISessionFactory
            {
                _services.AddSingleton<ISessionFactory, T>();
                return this;
            }

            /// <summary>
            /// Use an alternative strategy / configuration for opening IDocumentSession or IQuerySession
            /// objects in the application with a custom ISessionFactory type registered as scoped.
            ///
            /// Use this overload if the session creation needs to vary by application scope such as
            /// using a different tenant per HTTP request or if using some kind of scoped logging
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <returns></returns>
            public MartenConfigurationExpression BuildSessionsPerScopeWith<T>() where T : class, ISessionFactory
            {
                _services.AddScoped<ISessionFactory, T>();
                return this;
            }

            /// <summary>
            /// Eagerly build the application's DocumentStore during application
            /// bootstrapping rather than waiting for the first usage of IDocumentStore
            /// at runtime.
            /// </summary>
            /// <returns></returns>
            public IDocumentStore InitializeStore()
            {
                var store = new DocumentStore(_options);
                _services.AddSingleton<IDocumentStore>(store);

                return store;
            }

        }
    }

    /// <summary>
    /// Pluggable strategy for customizing how IDocumentSession / IQuerySession
    /// objects are created within an application.
    /// </summary>
    public interface ISessionFactory
    {
        /// <summary>
        /// Build new instances of IQuerySession on demand
        /// </summary>
        /// <returns></returns>
        IQuerySession QuerySession();

        /// <summary>
        /// Build new instances of IDocumentSession on demand
        /// </summary>
        /// <returns></returns>
        IDocumentSession OpenSession();
    }

    internal class DefaultSessionFactory: ISessionFactory
    {
        private readonly IDocumentStore _store;

        public DefaultSessionFactory(IDocumentStore store)
        {
            _store = store;
        }

        public IQuerySession QuerySession()
        {
            return _store.QuerySession();
        }

        public IDocumentSession OpenSession()
        {
            return _store.OpenSession();
        }
    }
}
