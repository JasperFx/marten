using System;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Internal;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Core;

#nullable enable
namespace Marten
{
    public static class MartenServiceCollectionExtensions
    {
        /// <summary>
        /// Apply additional configuration to a Marten DocumentStore. This is applied *after*
        /// AddMarten(), but before the DocumentStore is initialized
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IServiceCollection ConfigureMarten(this IServiceCollection services,
            Action<StoreOptions> configure)
        {
            return services.ConfigureMarten((s, opts) => configure(opts));
        }

        /// <summary>
        /// Apply additional configuration to a Marten DocumentStore. This is applied *after*
        /// AddMarten(), but before the DocumentStore is initialized
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IServiceCollection ConfigureMarten(this IServiceCollection services,
            Action<IServiceProvider, StoreOptions> configure)
        {
            var configureMarten = new LambdaConfigureMarten(configure);
            services.AddSingleton<IConfigureMarten>(configureMarten);
            return services;
        }

        /// <summary>
        /// Apply additional configuration to a Marten DocumentStore of type "T". This is applied *after*
        /// AddMartenStore<T>(), but before the actual DocumentStore for "T" is initialized
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IServiceCollection ConfigureMarten<T>(this IServiceCollection services,
            Action<IServiceProvider, StoreOptions> configure) where T : IDocumentStore
        {
            var configureMarten = new LambdaConfigureMarten<T>(configure);
            services.AddSingleton<IConfigureMarten<T>>(configureMarten);
            return services;
        }

        /// <summary>
        /// Apply additional configuration to a Marten DocumentStore of type "T". This is applied *after*
        /// AddMartenStore<T>(), but before the actual DocumentStore for "T" is initialized
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static IServiceCollection ConfigureMarten<T>(this IServiceCollection services,
            Action<StoreOptions> configure) where T : IDocumentStore
        {
            var configureMarten = new LambdaConfigureMarten<T>((s, opts) => configure(opts));
            services.AddSingleton<IConfigureMarten<T>>(configureMarten);
            return services;
        }

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
            services.AddMarten(s => options);
            return new MartenConfigurationExpression(services, options);
        }

        /// <summary>
        /// Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
        /// to your application by configuring a StoreOptions using services in your DI container
        /// </summary>
        /// <param name="optionSource">Func that will build out a StoreOptions with the applications IServiceProvider as the input</param>
        /// <returns></returns>
        public static MartenConfigurationExpression AddMarten(this IServiceCollection services, Func<IServiceProvider, StoreOptions> optionSource)
        {
            services.AddSingleton(s =>
            {
                var options = optionSource(s);
                var configures = s.GetServices<IConfigureMarten>();
                foreach (var configure in configures)
                {
                    configure.Configure(s, options);
                }

                var environment = s.GetService<IHostEnvironment>();
                if (environment != null)
                {
                    options.ReadHostEnvironment(environment);
                }

                return options;
            });

            services.AddSingleton<IDocumentStore>(s =>
            {
                var options = s.GetRequiredService<StoreOptions>();
                if (options.Logger().GetType() != typeof(NulloMartenLogger)) return new DocumentStore(options);
                var logger = s.GetService<ILogger<IDocumentStore>>() ?? new NullLogger<IDocumentStore>();
                options.Logger(new DefaultMartenLogger(logger));

                return new DocumentStore(options);
            });

            // This can be overridden by the expression following
            services.AddSingleton<ISessionFactory, DefaultSessionFactory>();

            services.AddScoped(s => s.GetRequiredService<ISessionFactory>().QuerySession());
            services.AddScoped(s => s.GetRequiredService<ISessionFactory>().OpenSession());

            services.AddSingleton<ICodeFileCollection>(s => (ICodeFileCollection)s.GetRequiredService<IDocumentStore>());
            services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<StoreOptions>());
            services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<StoreOptions>().EventGraph);

            return new MartenConfigurationExpression(services, null);
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

        /// <summary>
        /// Add a secondary IDocumentStore service to the container using only
        /// an interface "T" that should directly inherit from IDocumentStore
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static MartenStoreExpression<T> AddMartenStore<T>(this IServiceCollection services, Action<StoreOptions> configure) where T : class, IDocumentStore
        {
            return services.AddMartenStore<T>((s =>
            {
                var options = new StoreOptions();
                configure(options);

                return options;
            }));
        }

        /// <summary>
        /// Add a secondary IDocumentStore service to the container using only
        /// an interface "T" that should directly inherit from IDocumentStore
        /// </summary>
        /// <param name="services"></param>
        /// <param name="configure"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static MartenStoreExpression<T> AddMartenStore<T>(this IServiceCollection services, Func<IServiceProvider, StoreOptions> configure) where T : class, IDocumentStore
        {
            var stores = services.Select(x => x.ImplementationInstance).OfType<SecondaryDocumentStores>().FirstOrDefault();
            if (stores == null)
            {
                stores = new SecondaryDocumentStores();
                services.AddSingleton(stores);
                // I'm not proud of this, but you need the IServiceProvider in order to
                // build the generation rules
                services.AddSingleton<ICodeFileCollection>(s =>
                {
                    stores.Services = s;
                    return stores;
                });
            }

            var config = new SecondaryStoreConfig<T>(configure);
            stores.Add(config);

            services.AddSingleton<T>(s => config.Build(s));

            services.AddSingleton<ICodeFileCollection>(s => (ICodeFileCollection)s.GetRequiredService<T>());
            services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<T>().As<DocumentStore>().Options);
            services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<T>().As<DocumentStore>().Options.EventGraph);


            return new MartenStoreExpression<T>(services);
        }

        public class MartenStoreExpression<T> where T : IDocumentStore
        {
            public IServiceCollection Services { get; }

            public MartenStoreExpression(IServiceCollection services)
            {
                Services = services;
            }


            /// <summary>
            /// Adds the optimized artifact workflow to the store "T"
            /// </summary>
            /// <returns></returns>
            public MartenStoreExpression<T> OptimizeArtifactWorkflow()
            {
                return OptimizeArtifactWorkflow(TypeLoadMode.Auto);
            }

            /// <summary>
            /// Adds the optimized artifact workflow to the store "T"
            /// </summary>
            /// <param name="typeLoadMode"></param>
            /// <returns></returns>
            public MartenStoreExpression<T> OptimizeArtifactWorkflow(TypeLoadMode typeLoadMode)
            {
                Services.AddSingleton<IConfigureMarten<T>>(new OptimizedArtifactsWorkflow<T>(typeLoadMode));
                return this;
            }

            /// <summary>
            /// Register the Async Daemon hosted service to continuously attempt to update asynchronous event projections
            /// </summary>
            /// <param name="mode"></param>
            /// <returns></returns>
            public MartenStoreExpression<T> AddAsyncDaemon(DaemonMode mode)
            {
                Services.ConfigureMarten<T>(opts => opts.Projections.AsyncMode = mode);
                Services.AddSingleton<IHostedService, AsyncProjectionHostedService<T>>();

                return this;
            }

            /// <summary>
            /// Adds a hosted service to your .Net application that will attempt to apply any detected database changes before the
            /// rest of the application starts running
            /// </summary>
            /// <returns></returns>
            public MartenStoreExpression<T> ApplyAllDatabaseChangesOnStartup()
            {
                Services.Insert(0, new ServiceDescriptor(typeof(IHostedService), typeof(ApplyChangesOnStartup<T>), ServiceLifetime.Singleton));
                return this;
            }
        }

        public class MartenConfigurationExpression
        {
            private readonly StoreOptions? _options;

            internal MartenConfigurationExpression(IServiceCollection services, StoreOptions? options)
            {
                Services = services;
                _options = options;
            }

            /// <summary>
            /// Use an alternative strategy / configuration for opening IDocumentSession or IQuerySession
            /// objects in the application with a custom ISessionFactory type registered as a singleton
            /// </summary>
            /// <param name="lifetime">IoC service lifetime for the session factory. Default is Singleton, but use Scoped if you need to reference per-scope services</param>
            /// <typeparam name="T"></typeparam>
            /// <returns></returns>
            public MartenConfigurationExpression BuildSessionsWith<T>(ServiceLifetime lifetime = ServiceLifetime.Singleton ) where T : class, ISessionFactory
            {
                Services.Add(new ServiceDescriptor(typeof(ISessionFactory), typeof(T), lifetime));
                return this;
            }

            /// <summary>
            /// Adds a hosted service to your .Net application that will attempt to apply any detected database changes before the
            /// rest of the application starts running
            /// </summary>
            /// <returns></returns>
            public MartenConfigurationExpression ApplyAllDatabaseChangesOnStartup()
            {
                Services.Insert(0, new ServiceDescriptor(typeof(IHostedService), typeof(ApplyChangesOnStartup), ServiceLifetime.Singleton));
                return this;
            }


            /// <summary>
            /// Register the Async Daemon hosted service to continuously attempt to update asynchronous event projections
            /// </summary>
            /// <param name="mode"></param>
            /// <returns></returns>
            public MartenConfigurationExpression AddAsyncDaemon(DaemonMode mode)
            {
                Services.ConfigureMarten(opts => opts.Projections.AsyncMode = mode);
                Services.AddSingleton<IHostedService, AsyncProjectionHostedService>();

                return this;
            }

            /// <summary>
            /// Gets the IServiceCollection
            /// </summary>
            public IServiceCollection Services { get; }

            /// <summary>
            /// Use lightweight sessions by default for the injected IDocumentSession objects. Equivalent to IDocumentStore.LightweightSession();
            /// </summary>
            /// <returns></returns>
            public MartenConfigurationExpression UseLightweightSessions()
            {
                BuildSessionsWith<LightweightSessionFactory>();
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
                if (_options == null)
                    throw new InvalidOperationException(
                        "This operation is not valid when the StoreOptions is built by Func<IServiceProvider, StoreOptions>");

                var store = new DocumentStore(_options);
                Services.AddSingleton<IDocumentStore>(store);

                return store;
            }

            /// <summary>
            /// Adds the optimized artifact workflow to this store. TODO -- LINK TO DOCS
            /// </summary>
            /// <returns></returns>
            public MartenConfigurationExpression OptimizeArtifactWorkflow()
            {

                return OptimizeArtifactWorkflow(TypeLoadMode.Auto);
            }

            /// <summary>
            /// Adds the optimized artifact workflow to this store with ability to override the TypeLoadMode in "Production" mode. TODO -- LINK TO DOCS
            /// </summary>
            /// <param name="typeLoadMode"></param>
            /// <returns></returns>
            public MartenConfigurationExpression OptimizeArtifactWorkflow(TypeLoadMode typeLoadMode)
            {
                var configure = new OptimizedArtifactsWorkflow(typeLoadMode);
                Services.AddSingleton<IConfigureMarten>(configure);

                return this;
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

    internal class LightweightSessionFactory: ISessionFactory
    {
        private readonly IDocumentStore _store;

        public LightweightSessionFactory(IDocumentStore store)
        {
            _store = store;
        }

        public IQuerySession QuerySession()
        {
            return _store.QuerySession();
        }

        public IDocumentSession OpenSession()
        {
            return _store.LightweightSession();
        }
    }

    /// <summary>
    /// Mechanism to register additional Marten configuration that is applied after AddMarten()
    /// configuration, but before DocumentStore is initialized
    /// </summary>
    public interface IConfigureMarten
    {
        void Configure(IServiceProvider services, StoreOptions options);
    }

    internal class LambdaConfigureMarten: IConfigureMarten
    {
        private readonly Action<IServiceProvider, StoreOptions> _configure;

        public LambdaConfigureMarten(Action<IServiceProvider, StoreOptions> configure)
        {
            _configure = configure;
        }

        public void Configure(IServiceProvider services, StoreOptions options)
        {
            _configure(services, options);
        }
    }

    internal class LambdaConfigureMarten<T>: LambdaConfigureMarten, IConfigureMarten<T> where T : IDocumentStore
    {
        public LambdaConfigureMarten(Action<IServiceProvider, StoreOptions> configure) : base(configure)
        {
        }
    }

    internal class OptimizedArtifactsWorkflow: IConfigureMarten
    {
        private readonly TypeLoadMode _productionMode;

        public OptimizedArtifactsWorkflow(TypeLoadMode productionMode)
        {
            _productionMode = productionMode;
        }

        public void Configure(IServiceProvider services, StoreOptions options)
        {
            var environment = services.GetRequiredService<IHostEnvironment>();

            if (environment.IsDevelopment())
            {
                options.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                options.GeneratedCodeMode = TypeLoadMode.Auto;
            }
            else
            {
                options.AutoCreateSchemaObjects = AutoCreate.None;
                options.GeneratedCodeMode = _productionMode;


                options.SourceCodeWritingEnabled = false;
            }
        }
    }

    internal class OptimizedArtifactsWorkflow<T>: OptimizedArtifactsWorkflow, IConfigureMarten<T>
        where T : IDocumentStore
    {
        public OptimizedArtifactsWorkflow(TypeLoadMode productionMode) : base(productionMode)
        {
        }
    }
}
