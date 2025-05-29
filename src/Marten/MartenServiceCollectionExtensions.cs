#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.CommandLine.Descriptions;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using JasperFx.RuntimeCompiler;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Projections;
using Marten.Internal;
using Marten.Schema;
using Marten.Services;
using Marten.Sessions;
using Marten.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Weasel.Core.Migrations;
using Weasel.Core.MultiTenancy;

namespace Marten;

public static class MartenServiceCollectionExtensions
{
    /// <summary>
    ///     Apply additional configuration to a Marten DocumentStore. This is applied *after*
    ///     AddMarten(), but before the DocumentStore is initialized
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configure"></param>
    /// <returns></returns>
    public static IServiceCollection ConfigureMartenWithServices<T>(this IServiceCollection services)
        where T : class, IAsyncConfigureMarten
    {
        services.EnsureAsyncConfigureMartenApplicationIsRegistered();
        services.AddSingleton<IAsyncConfigureMarten, T>();
        return services;
    }

    /// <summary>
    ///     Apply additional configuration to a Marten DocumentStore. This is applied *after*
    ///     AddMarten(), but before the DocumentStore is initialized
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
    ///     Apply additional configuration to a Marten DocumentStore. This is applied *after*
    ///     AddMarten(), but before the DocumentStore is initialized
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
    ///     Apply additional configuration to a Marten DocumentStore of type "T". This is applied *after*
    ///     AddMartenStore<T>(), but before the actual DocumentStore for "T" is initialized
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
    ///     Apply additional configuration to a Marten DocumentStore of type "T". This is applied *after*
    ///     AddMartenStore<T>(), but before the actual DocumentStore for "T" is initialized
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
    ///     Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
    ///     to your application with the given Postgresql connection string and Marten
    ///     defaults
    /// </summary>
    /// <remarks>
    ///     You need to configure connection settings through DI, e.g. by calling `UseNpgsqlDataSource`
    ///     and configuring `NpqsqlDataSource` with `AddNpgsqlDataSource` from `Npgsql.DependencyInjection`
    /// </remarks>
    /// <param name="services"></param>
    /// <returns></returns>
    public static MartenConfigurationExpression AddMarten(this IServiceCollection services)
    {
        return services.AddMarten(new StoreOptions());
    }

    /// <summary>
    ///     Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
    ///     to your application with the given Postgresql connection string and Marten
    ///     defaults
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
    ///     Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
    ///     to your application using the configured StoreOptions
    /// </summary>
    /// <param name="services"></param>
    /// <param name="options">The Marten configuration for this application</param>
    /// <returns></returns>
    public static MartenConfigurationExpression AddMarten(
        this IServiceCollection services,
        StoreOptions options
    )
    {
        services.AddMarten(s => options);
        return new MartenConfigurationExpression(services, options);
    }

    /// <summary>
    ///     Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
    ///     to your application by configuring a StoreOptions using services in your DI container
    /// </summary>
    /// <param name="optionSource">Func that will build out a StoreOptions with the applications IServiceProvider as the input</param>
    /// <returns></returns>
    public static MartenConfigurationExpression AddMarten(
        this IServiceCollection services,
        Func<IServiceProvider, StoreOptions> optionSource
    )
    {
        services.AddJasperFx();
        services.AddSingleton<ISystemPart, MartenSystemPart>();
        services.AddSingleton<IEventStore>(s => (IEventStore)s.GetRequiredService<IDocumentStore>());
        services.AddSingleton<IAssemblyGenerator, AssemblyGenerator>();
        services.AddSingleton(s =>
        {
            var options = optionSource(s);
            var configures = s.GetServices<IConfigureMarten>();
            foreach (var configure in configures) configure.Configure(s, options);

            options.ReadJasperFxOptions(s.GetService<JasperFxOptions>());

            options.InitialData.AddRange(s.GetServices<IInitialData>());

            return options;
        });

        services.AddSingleton<IDocumentStore>(s =>
        {
            var options = s.GetRequiredService<StoreOptions>();
            if (options.Logger().GetType() != typeof(NulloMartenLogger))
            {
                return new DocumentStore(options);
            }

            var logger = s.GetService<ILogger<IDocumentStore>>() ?? new NullLogger<IDocumentStore>();
            options.Logger(new DefaultMartenLogger(logger));

            options.LogFactory = s.GetService<ILoggerFactory>();

            return new DocumentStore(options);
        });

        services.AddSingleton<IMasterTableMultiTenancy>(s => (IMasterTableMultiTenancy)s.GetRequiredService<IDocumentStore>());

        // This can be overridden by the expression following
        services.AddSingleton<ISessionFactory, DefaultSessionFactory>(sp =>
        {
            var logger = sp.GetService<ILogger<DefaultSessionFactory>>() ?? new NullLogger<DefaultSessionFactory>();
            return new DefaultSessionFactory(sp.GetRequiredService<IDocumentStore>(), logger);
        });

        services.AddScoped(s => s.GetRequiredService<ISessionFactory>().QuerySession());
        services.AddScoped(s => s.GetRequiredService<ISessionFactory>().OpenSession());

        services.AddSingleton(s => (ICodeFileCollection)s.GetRequiredService<IDocumentStore>());
        services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<StoreOptions>());

        services.AddSingleton<IDatabaseSource>(s =>
            s.GetRequiredService<IDocumentStore>().As<DocumentStore>().Tenancy);

        return new MartenConfigurationExpression(services, null);
    }

    /// <summary>
    ///     Add Marten IDocumentStore, IDocumentSession, and IQuerySession service registrations
    ///     to your application using the configured StoreOptions
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configure"></param>
    /// <returns></returns>
    public static MartenConfigurationExpression AddMarten(
        this IServiceCollection services,
        Action<StoreOptions> configure
    )
    {
        var options = new StoreOptions();
        configure(options);

        return services.AddMarten(options);
    }

    /// <summary>
    ///     Add a secondary IDocumentStore service to the container using only
    ///     an interface "T" that should directly inherit from IDocumentStore
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configure"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static MartenStoreExpression<T> AddMartenStore<T>(
        this IServiceCollection services,
        Action<StoreOptions> configure
    ) where T : class, IDocumentStore
    {
        return services.AddMartenStore<T>(s =>
        {
            var options = new StoreOptions();
            configure(options);

            return options;
        });
    }

    /// <summary>
    ///     Add a secondary IDocumentStore service to the container using only
    ///     an interface "T" that should directly inherit from IDocumentStore
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configure"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static MartenStoreExpression<T> AddMartenStore<T>(this IServiceCollection services,
        Func<IServiceProvider, StoreOptions> configure) where T : class, IDocumentStore
    {
        services.AddJasperFx();
        services.AddSingleton<IDocumentStoreSource, DocumentStoreSource<T>>();

        services.AddSingleton<IAssemblyGenerator, AssemblyGenerator>();

        services.AddSingleton<ISystemPart, MartenSystemPart<T>>();

        services.AddSingleton<IEventStore>(s => (IEventStore)s.GetRequiredService<T>());

        var stores = services
            .Where(x => !x.IsKeyedService)
            .Select(x => x.ImplementationInstance)
            .OfType<SecondaryDocumentStores>().FirstOrDefault();

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

        services.AddSingleton<IDatabaseSource>(s => s.GetRequiredService<T>().As<DocumentStore>().Tenancy);

        var config = new SecondaryStoreConfig<T>(configure);
        stores.Add(config);

        services.AddSingleton<T>(s => config.Build(s));


        services.AddSingleton<IMasterTableMultiTenancy>(s => (IMasterTableMultiTenancy)s.GetRequiredService<T>());


        services.AddSingleton<ICodeFileCollection>(s =>
        {
            var options = config.BuildStoreOptions(s);
            return new DocumentStore(options);
        });

        services.AddSingleton<ICodeFileCollection>(s => config.BuildStoreOptions(s));

        return new MartenStoreExpression<T>(services);
    }

    internal static IReadOnlyList<IDocumentStore> AllDocumentStores(this IHost host)
    {
        return host.Services.AllDocumentStores();
    }

    public static IReadOnlyList<IDocumentStore> AllDocumentStores(this IServiceProvider services)
    {
        var list = new List<IDocumentStore>();
        var store = services.GetService<IDocumentStore>();
        if (store != null)
        {
            list.Add(store);
        }

        list.AddRange(services.GetServices<IDocumentStoreSource>().Select(x => x.Resolve(services)));

        return list;
    }

    internal static void EnsureAsyncConfigureMartenApplicationIsRegistered(this IServiceCollection services)
    {
        if (!services.Any(
                x => x.ServiceType == typeof(IHostedService) &&
                     x.ImplementationType == typeof(AsyncConfigureMartenApplication)))
        {
            services.Insert(0,
                new ServiceDescriptor(typeof(IHostedService), typeof(AsyncConfigureMartenApplication),
                    ServiceLifetime.Singleton));
        }
    }

    internal static void EnsureMartenActivatorIsRegistered(this IServiceCollection services)
    {
        if (!services.Any(
                x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(MartenActivator)))
        {
            var descriptor = services.FirstOrDefault(x =>
                x.ServiceType == typeof(IHostedService) &&
                x.ImplementationType == typeof(AsyncConfigureMartenApplication));

            if (descriptor != null)
            {
                var index = services.IndexOf(descriptor);
                services.Insert(index + 1,
                    new ServiceDescriptor(typeof(IHostedService), typeof(MartenActivator), ServiceLifetime.Singleton));
            }
            else
            {
                services.Insert(0,
                    new ServiceDescriptor(typeof(IHostedService), typeof(MartenActivator), ServiceLifetime.Singleton));
            }
        }
    }

    internal static void EnsureMartenActivatorIsRegistered<T>(this IServiceCollection services) where T : IDocumentStore
    {
        if (!services.Any(
                x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(MartenActivator<T>)))
        {
            services.Insert(0,
                new ServiceDescriptor(typeof(IHostedService), typeof(MartenActivator<T>), ServiceLifetime.Singleton));
        }
    }

    /// <summary>
    ///     Adds initial data sets to the separate Marten store of type "T" and ensures that they will be
    ///     executed upon IHost initialization
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static IServiceCollection InitializeMartenWith<T>(this IServiceCollection services,
        params IInitialData[] data) where T : IDocumentStore
    {
        services.EnsureMartenActivatorIsRegistered<T>();
        services.ConfigureMarten<T>(opts => opts.InitialData.AddRange(data));
        return services;
    }

    /// <summary>
    ///     Registers type T as a singleton against IInitialData to be used in IHost activation
    ///     to apply changes or at least actions against the as built IDocumentStore
    /// </summary>
    /// <typeparam name="TData">The type that implements IInitialData</typeparam>
    /// <returns></returns>
    public static IServiceCollection InitializeMartenWith<TStore, TData>(this IServiceCollection services)
        where TData : class, IInitialData where TStore : IDocumentStore
    {
        services.EnsureMartenActivatorIsRegistered<TStore>();
        services.AddSingleton<TData>();
        services.AddSingleton<IConfigureMarten<TStore>, AddInitialData<TStore, TData>>();
        return services;
    }

    /// <summary>
    ///     Adds initial data sets to the Marten store and ensures that they will be
    ///     executed upon IHost initialization
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static IServiceCollection InitializeMartenWith(this IServiceCollection services, params IInitialData[] data)
    {
        services.EnsureMartenActivatorIsRegistered();
        services.ConfigureMarten(opts => opts.InitialData.AddRange(data));
        return services;
    }

    /// <summary>
    ///     Registers type T as a singleton against IInitialData to be used in IHost activation
    ///     to apply changes or at least actions against the as built IDocumentStore
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static IServiceCollection InitializeMartenWith<T>(this IServiceCollection services)
        where T : class, IInitialData
    {
        services.EnsureMartenActivatorIsRegistered();
        services.AddSingleton<IInitialData, T>();
        return services;
    }


    public class MartenStoreExpression<T> where T : class, IDocumentStore
    {
        public MartenStoreExpression(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }

        /// <summary>
        ///     Register the Async Daemon hosted service to continuously attempt to update asynchronous event projections
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        public MartenStoreExpression<T> AddAsyncDaemon(DaemonMode mode)
        {
            Services.ConfigureMarten<T>(opts => opts.Projections.AsyncMode = mode);
            if (mode != DaemonMode.Disabled)
            {
                Services.AddSingleton<IProjectionCoordinator<T>, ProjectionCoordinator<T>>();
                Services.AddSingleton<IHostedService>(s => s.GetRequiredService<IProjectionCoordinator<T>>());
            }

            return this;
        }

        /// <summary>
        ///     Adds a hosted service to your .Net application that will attempt to apply any detected database changes before the
        ///     rest of the application starts running
        /// </summary>
        /// <returns></returns>
        public MartenStoreExpression<T> ApplyAllDatabaseChangesOnStartup()
        {
            Services.EnsureMartenActivatorIsRegistered<T>();

            Services.ConfigureMarten<T>(opts => opts.ShouldApplyChangesOnStartup = true);

            return this;
        }


        /// <summary>
        ///     Adds initial data sets to the Marten store and ensures that they will be
        ///     executed upon IHost initialization
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public MartenStoreExpression<T> InitializeWith(params IInitialData[] data)
        {
            Services.EnsureMartenActivatorIsRegistered<T>();
            Services.ConfigureMarten<T>(opts => opts.InitialData.AddRange(data));
            return this;
        }

        /// <summary>
        ///     Registers type T as a singleton against IInitialData to be used in IHost activation
        ///     to apply changes or at least actions against the as built IDocumentStore
        /// </summary>
        /// <typeparam name="TData">The type that implements IInitialData</typeparam>
        /// <returns></returns>
        public MartenStoreExpression<T> InitializeWith<TData>() where TData : class, IInitialData
        {
            Services.EnsureMartenActivatorIsRegistered<T>();
            Services.AddSingleton<TData>();
            Services.AddSingleton<IConfigureMarten<T>, AddInitialData<T, TData>>();
            return this;
        }

        /// <summary>
        ///     Add a projection to this application that requires IoC services. The projection itself will
        ///     be created with the application's IoC container
        /// </summary>
        /// <param name="lifecycle">The projection lifecycle for Marten</param>
        /// <param name="lifetime">
        ///     The IoC lifecycle for the projection instance. Note that the Transient lifetime will still be
        ///     treated as Scoped
        /// </param>
        /// <param name="configure">Optional confiuration of the projection name, version, event filtering, and async execution</param>
        /// ///
        /// <typeparam name="TProjection">The type of projection to add</typeparam>
        /// <returns></returns>
        public MartenStoreExpression<T> AddProjectionWithServices<TProjection>(ProjectionLifecycle lifecycle,
            ServiceLifetime lifetime, Action<ProjectionBase>? configure = null) where TProjection : class, IMartenRegistrable
        {
            TProjection.Register<TProjection, T>(Services, lifecycle, lifetime, configure);

            return this;
        }

        /// <summary>
        ///     Add a projection to this application that requires IoC services. The projection itself will
        ///     be created with the application's IoC container
        /// </summary>
        /// <param name="lifecycle">The projection lifecycle for Marten</param>
        /// <param name="lifetime">
        ///     The IoC lifecycle for the projection instance. Note that the Transient lifetime will still be
        ///     treated as Scoped
        /// </param>
        /// <typeparam name="TProjection">The type of projection to add</typeparam>
        /// <returns></returns>
        [Obsolete("Favor the overload that uses Action<ProjectionBase>. This will be removed in Marten 9")]
        public MartenStoreExpression<T> AddProjectionWithServices<TProjection>(ProjectionLifecycle lifecycle,
            ServiceLifetime lifetime, string projectionName) where TProjection : class, IMartenRegistrable
        {
            return AddProjectionWithServices<TProjection>(lifecycle, lifetime, x => x.Name = projectionName);
        }


        /// <summary>
        ///     Add a subscription to this Marten store that will require resolution
        ///     from the application's IoC container in order to function correctly
        /// </summary>
        /// <param name="lifetime">IoC service lifetime</param>
        /// <param name="configure">Optional configuration of the subscription within Marten</param>
        /// <typeparam name="T">The type of projection to add</typeparam>
        /// <returns></returns>
        public MartenStoreExpression<T> AddSubscriptionWithServices<TSubscription>(ServiceLifetime lifetime,
            Action<ISubscriptionOptions>? configure = null) where TSubscription : class, ISubscription
        {
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    Services.AddSingleton<TSubscription>();
                    Services.ConfigureMarten<T>((s, opts) =>
                    {
                        var subscription = s.GetRequiredService<TSubscription>();
                        if (subscription is SubscriptionBase subscriptionBase)
                        {
                            configure?.Invoke(subscriptionBase);
                            opts.Projections.Subscribe(subscriptionBase);
                        }
                        else
                        {
                            opts.Projections.Subscribe(subscription, configure);
                        }
                    });
                    break;

                case ServiceLifetime.Transient:
                case ServiceLifetime.Scoped:
                    Services.AddScoped<TSubscription>();
                    Services.ConfigureMarten<T>((s, opts) =>
                    {
                        var subscription = new ScopedSubscriptionServiceWrapper<TSubscription>(s);
                        opts.Projections.Subscribe(subscription, configure);
                    });
                    break;
            }

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
        ///     Gets the IServiceCollection
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        ///     Use an alternative strategy / configuration for opening IDocumentSession or IQuerySession
        ///     objects in the application with a custom ISessionFactory type registered as a singleton
        /// </summary>
        /// <param name="lifetime">
        ///     IoC service lifetime for the session factory. Default is Singleton, but use Scoped if you need
        ///     to reference per-scope services
        /// </param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public MartenConfigurationExpression BuildSessionsWith<T>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
            where T : class, ISessionFactory
        {
            Services.Add(new ServiceDescriptor(typeof(ISessionFactory), typeof(T), lifetime));
            return this;
        }

        /// <summary>
        ///     Adds a hosted service to your .Net application that will attempt to apply any detected database changes before the
        ///     rest of the application starts running
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression ApplyAllDatabaseChangesOnStartup()
        {
            Services.EnsureMartenActivatorIsRegistered();

            Services.ConfigureMarten(opts => opts.ShouldApplyChangesOnStartup = true);

            return this;
        }

        /// <summary>
        ///     Adds a hosted service to your .Net application that will assert that database matches configuration before the
        ///     rest of the application starts running. Prevents the application from starting if database does not match
        ///     configuration.
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression AssertDatabaseMatchesConfigurationOnStartup()
        {
            Services.EnsureMartenActivatorIsRegistered();

            Services.ConfigureMarten(opts => opts.ShouldAssertDatabaseMatchesConfigurationOnStartup = true);

            return this;
        }

        /// <summary>
        ///     Register the Async Daemon hosted service to continuously attempt to update asynchronous event projections
        /// </summary>
        /// <param name="mode"></param>
        /// <returns></returns>
        public MartenConfigurationExpression AddAsyncDaemon(DaemonMode mode)
        {
            Services.ConfigureMarten(opts => opts.Projections.AsyncMode = mode);

            if (mode != DaemonMode.Disabled)
            {
                Services.AddSingleton<IProjectionCoordinator, ProjectionCoordinator>();
                Services.AddSingleton<IHostedService>(s => s.GetRequiredService<IProjectionCoordinator>());
            }

            return this;
        }

        /// <summary>
        ///     Use lightweight sessions by default for the injected IDocumentSession objects. Equivalent to
        ///     IDocumentStore.LightweightSession();
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression UseLightweightSessions()
        {
            return BuildSessionsWith<LightweightSessionFactory>();
        }

        /// <summary>
        ///     Use identity sessions by default for the injected IDocumentSession objects. Equivalent to
        ///     IDocumentStore.IdentitySession();
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression UseIdentitySessions()
        {
            return BuildSessionsWith<IdentitySessionFactory>();
        }

        /// <summary>
        ///     Use dirty-tracked sessions by default for the injected IDocumentSession objects. Equivalent to
        ///     IDocumentStore.DirtyTrackedSession();
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression UseDirtyTrackedSessions()
        {
            return BuildSessionsWith<DirtyTrackedSessionFactory>();
        }

        /// <summary>
        ///     Use configured NpgsqlDataSource from DI container
        /// </summary>
        /// <param name="serviceKey">NpgsqlDataSource service key as registered in DI</param>
        /// <returns></returns>
        public MartenConfigurationExpression UseNpgsqlDataSource(object? serviceKey = null)
        {
            Services.ConfigureMarten((sp, opts) =>
                opts.Connection(
                    serviceKey != null
                        ? sp.GetRequiredKeyedService<NpgsqlDataSource>(serviceKey)
                        : sp.GetRequiredService<NpgsqlDataSource>()
                )
            );
            return this;
        }

        /// <summary>
        ///     Use configured NpgsqlDataSource from DI container
        /// </summary>
        /// <param name="dataSourceBuilderFactory">configuration of the data source builder</param>
        /// <param name="serviceKey">NpgsqlDataSource service key as registered in DI</param>
        /// <returns></returns>
        public MartenConfigurationExpression UseNpgsqlDataSource(
            Func<string, NpgsqlDataSourceBuilder> dataSourceBuilderFactory,
            object? serviceKey = null
        )
        {
            Services.ConfigureMarten((sp, opts) =>
                opts.Connection(
                    dataSourceBuilderFactory,
                    serviceKey != null
                        ? sp.GetRequiredKeyedService<NpgsqlDataSource>(serviceKey)
                        : sp.GetRequiredService<NpgsqlDataSource>()
                )
            );
            return this;
        }

        /// <summary>
        ///     Adds initial data sets to the Marten store and ensures that they will be
        ///     executed upon IHost initialization
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public MartenConfigurationExpression InitializeWith(params IInitialData[] data)
        {
            Services.EnsureMartenActivatorIsRegistered();
            Services.ConfigureMarten(opts => opts.InitialData.AddRange(data));
            return this;
        }

        /// <summary>
        ///     Registers type T as a singleton against IInitialData to be used in IHost activation
        ///     to apply changes or at least actions against the as built IDocumentStore
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public MartenConfigurationExpression InitializeWith<T>() where T : class, IInitialData
        {
            Services.EnsureMartenActivatorIsRegistered();
            Services.AddSingleton<IInitialData, T>();
            return this;
        }

        /// <summary>
        ///     Add a projection to this application that requires IoC services. The projection itself will
        ///     be created with the application's IoC container
        /// </summary>
        /// <param name="lifecycle">The projection lifecycle for Marten</param>
        /// <param name="lifetime">
        ///     The IoC lifecycle for the projection instance. Note that the Transient lifetime will still be
        ///     treated as Scoped
        /// </param>
        /// <typeparam name="T"></typeparam>
        /// <param name="configure">Optional configuration of the projection behavior including the name, version, event filtering, and async execution behavior</param>
        /// <returns></returns>
        public MartenConfigurationExpression AddProjectionWithServices<T>(ProjectionLifecycle lifecycle,
            ServiceLifetime lifetime, Action<ProjectionBase>? configure = null) where T : class, IMartenRegistrable
        {
            T.Register<T>(Services, lifecycle, lifetime, configure);
            return this;
        }

        /// <summary>
        ///     Add a projection to this application that requires IoC services. The projection itself will
        ///     be created with the application's IoC container
        /// </summary>
        /// <param name="lifecycle">The projection lifecycle for Marten</param>
        /// <param name="lifetime">
        ///     The IoC lifecycle for the projection instance. Note that the Transient lifetime will still be
        ///     treated as Scoped
        /// </param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public MartenConfigurationExpression AddProjectionWithServices<T>(ProjectionLifecycle lifecycle,
            ServiceLifetime lifetime, string projectionName) where T : class, IMartenRegistrable
        {
            return AddProjectionWithServices<T>(lifecycle, lifetime, x => x.Name = projectionName);
        }


        /// <summary>
        ///     Add a subscription to this Marten store that will require resolution
        ///     from the application's IoC container in order to function correctly
        /// </summary>
        /// <param name="lifetime">IoC service lifetime</param>
        /// <param name="configure">Optional configuration of the subscription within Marten</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public MartenConfigurationExpression AddSubscriptionWithServices<T>(
            ServiceLifetime lifetime, Action<ISubscriptionOptions>? configure = null) where T : class, ISubscription
        {
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    Services.AddSingleton<T>();
                    Services.ConfigureMarten((s, opts) =>
                    {
                        var subscription = s.GetRequiredService<T>();
                        if (subscription is SubscriptionBase subscriptionBase)
                        {
                            configure?.Invoke(subscriptionBase);
                            opts.Projections.Subscribe(subscriptionBase);
                        }
                        else
                        {
                            opts.Projections.Subscribe(subscription, configure);
                        }
                    });
                    break;

                case ServiceLifetime.Transient:
                case ServiceLifetime.Scoped:
                    Services.AddScoped<T>();
                    Services.ConfigureMarten((s, opts) =>
                    {
                        var subscription = new ScopedSubscriptionServiceWrapper<T>(s);
                        opts.Projections.Subscribe(subscription, configure);
                    });
                    break;
            }

            return this;
        }
    }

    internal class AddInitialData<T, TData>: IConfigureMarten<T> where T : IDocumentStore where TData : IInitialData
    {
        private readonly TData _data;

        public AddInitialData(TData data)
        {
            _data = data;
        }

        public void Configure(IServiceProvider services, StoreOptions options)
        {
            options.InitialData.Add(_data);
        }
    }
}

#region sample_IConfigureMarten

/// <summary>
///     Mechanism to register additional Marten configuration that is applied after AddMarten()
///     configuration, but before DocumentStore is initialized
/// </summary>
public interface IConfigureMarten
{
    void Configure(IServiceProvider services, StoreOptions options);
}

#endregion

#region sample_IAsyncConfigureMarten

/// <summary>
///     Mechanism to register additional Marten configuration that is applied after AddMarten()
///     configuration, but before DocumentStore is initialized when you need to utilize some
///     kind of asynchronous services like Microsoft's FeatureManagement feature to configure Marten
/// </summary>
public interface IAsyncConfigureMarten
{
    ValueTask Configure(StoreOptions options, CancellationToken cancellationToken);
}

#endregion

internal class AsyncConfigureMartenApplication: IHostedService
{
    private readonly IList<IAsyncConfigureMarten> _configures;
    private readonly StoreOptions _options;

    public AsyncConfigureMartenApplication(IEnumerable<IAsyncConfigureMarten> configures, StoreOptions options)
    {
        _configures = configures.ToList();
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var configure in _configures)
            await configure.Configure(_options, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
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
    public LambdaConfigureMarten(Action<IServiceProvider, StoreOptions> configure): base(configure)
    {
    }
}

internal interface IDocumentStoreSource
{
    IDocumentStore Resolve(IServiceProvider serviceProvider);
}

internal class DocumentStoreSource<T>: IDocumentStoreSource where T : IDocumentStore
{
    public IDocumentStore Resolve(IServiceProvider services)
    {
        return services.GetRequiredService<T>();
    }
}
