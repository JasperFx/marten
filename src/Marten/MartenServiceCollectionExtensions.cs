#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Internal;
using Marten.Schema;
using Marten.Services;
using Marten.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Core;
using Weasel.Core.Migrations;

namespace Marten;

public static class MartenServiceCollectionExtensions
{
    /// <summary>
    ///     Meant for testing scenarios to "help" .Net understand where the IHostEnvironment for the
    ///     Host. You may have to specify the relative path to the entry project folder from the AppContext.BaseDirectory
    /// </summary>
    /// <param name="services"></param>
    /// <param name="assembly"></param>
    /// <param name="hintPath"></param>
    /// <returns></returns>
    public static IHostBuilder UseApplicationProject(this IHostBuilder builder, Assembly assembly,
        string? hintPath = null)
    {
        return builder.ConfigureServices((c, services) => services.SetApplicationProject(assembly, hintPath));
    }

    /// <summary>
    ///     Meant for testing scenarios to "help" .Net understand where the IHostEnvironment for the
    ///     Host. You may have to specify the relative path to the entry project folder from the AppContext.BaseDirectory
    /// </summary>
    /// <param name="services"></param>
    /// <param name="assembly"></param>
    /// <param name="hintPath"></param>
    /// <returns></returns>
    [Obsolete(
        "Yeah, this didn't work so well in some hosting setups. Prefer StoreOptions.SetApplicationProject() instead if necessary")]
    public static IServiceCollection SetApplicationProject(this IServiceCollection services, Assembly assembly,
        string? hintPath = null)
    {
        var environment = services.Select(x => x.ImplementationInstance)
            .OfType<IHostEnvironment>().LastOrDefault();

        var applicationName = assembly.GetName().Name;

        // There are possible project setups where IHostEnvironment is
        // not available
        if (environment != null)
        {
            environment.ApplicationName = applicationName;
        }

        var path = AppContext.BaseDirectory.ToFullPath();
        if (hintPath.IsNotEmpty())
        {
            path = path.AppendPath(hintPath).ToFullPath();
        }
        else
        {
            path = path.TrimEnd(Path.DirectorySeparatorChar);
            while (!path.EndsWith("bin"))
            {
                path = path.ParentDirectory();
            }

            // Go up once to get to the test project directory, then up again to the "src" level,
            // then "down" to the application directory
            path = path.ParentDirectory().ParentDirectory().AppendPath(applicationName);
        }

        environment.ContentRootPath = path;

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
    public static MartenConfigurationExpression AddMarten(this IServiceCollection services, StoreOptions options)
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
    public static MartenConfigurationExpression AddMarten(this IServiceCollection services,
        Func<IServiceProvider, StoreOptions> optionSource)
    {
        services.AddSingleton(s =>
        {
            var options = optionSource(s);
            var configures = s.GetServices<IConfigureMarten>();
            foreach (var configure in configures) configure.Configure(s, options);

            var environment = s.GetService<IHostEnvironment>();
            if (environment != null)
            {
                options.ReadHostEnvironment(environment);
            }

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

            return new DocumentStore(options);
        });

        // This can be overridden by the expression following
        services.AddSingleton<ISessionFactory, DefaultSessionFactory>();

        services.AddScoped(s => s.GetRequiredService<ISessionFactory>().QuerySession());
        services.AddScoped(s => s.GetRequiredService<ISessionFactory>().OpenSession());

        services.AddSingleton(s => (ICodeFileCollection)s.GetRequiredService<IDocumentStore>());
        services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<StoreOptions>());
        services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<StoreOptions>().EventGraph);

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
    public static MartenConfigurationExpression AddMarten(this IServiceCollection services,
        Action<StoreOptions> configure)
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
    public static MartenStoreExpression<T> AddMartenStore<T>(this IServiceCollection services,
        Action<StoreOptions> configure) where T : class, IDocumentStore
    {
        services.AddSingleton<IDocumentStoreSource, DocumentStoreSource<T>>();

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

        services.AddSingleton<IDatabaseSource>(s => s.GetRequiredService<T>().As<DocumentStore>().Tenancy);

        var config = new SecondaryStoreConfig<T>(configure);
        stores.Add(config);

        services.AddSingleton(s => config.Build(s));

        services.AddSingleton(s => (ICodeFileCollection)s.GetRequiredService<T>());
        services.AddSingleton<ICodeFileCollection>(s => s.GetRequiredService<T>().As<DocumentStore>().Options);
        services.AddSingleton<ICodeFileCollection>(
            s => s.GetRequiredService<T>().As<DocumentStore>().Options.EventGraph);


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

    internal static void EnsureMartenActivatorIsRegistered(this IServiceCollection services)
    {
        if (!services.Any(
                x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(MartenActivator)))
        {
            services.Insert(0,
                new ServiceDescriptor(typeof(IHostedService), typeof(MartenActivator), ServiceLifetime.Singleton));
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


    public class MartenStoreExpression<T> where T : IDocumentStore
    {
        public MartenStoreExpression(IServiceCollection services)
        {
            Services = services;
        }

        public IServiceCollection Services { get; }


        /// <summary>
        ///     Adds the optimized artifact workflow to the store "T"
        /// </summary>
        /// <returns></returns>
        public MartenStoreExpression<T> OptimizeArtifactWorkflow()
        {
            return OptimizeArtifactWorkflow(TypeLoadMode.Auto);
        }


        /// <summary>
        ///     Adds the optimized artifact workflow to the store "T"
        /// </summary>
        /// <param name="developmentEnvironment"></param>
        /// <returns></returns>
        public MartenStoreExpression<T> OptimizeArtifactWorkflow(string developmentEnvironment)
        {
            return OptimizeArtifactWorkflow(TypeLoadMode.Auto, developmentEnvironment);
        }


        /// <summary>
        ///     Adds the optimized artifact workflow to the store "T"
        /// </summary>
        /// <param name="typeLoadMode"></param>
        /// <returns></returns>
        public MartenStoreExpression<T> OptimizeArtifactWorkflow(TypeLoadMode typeLoadMode)
        {
            Services.AddSingleton<IConfigureMarten<T>>(new OptimizedArtifactsWorkflow<T>(typeLoadMode));
            return this;
        }


        /// <summary>
        ///     Adds the optimized artifact workflow to the store "T"
        /// </summary>
        /// <param name="typeLoadMode"></param>
        /// <param name="developmentEnvironment"></param>
        /// <returns></returns>
        public MartenStoreExpression<T> OptimizeArtifactWorkflow(TypeLoadMode typeLoadMode,
            string developmentEnvironment)
        {
            Services.AddSingleton<IConfigureMarten<T>>(
                new OptimizedArtifactsWorkflow<T>(typeLoadMode, developmentEnvironment));
            return this;
        }

        /// <summary>
        ///     Register the Async Daemon hosted service to continuously attempt to update asynchronous event projections
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
            Services.AddSingleton<IHostedService, AsyncProjectionHostedService>();

            return this;
        }

        /// <summary>
        ///     Use lightweight sessions by default for the injected IDocumentSession objects. Equivalent to
        ///     IDocumentStore.LightweightSession();
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression UseLightweightSessions() =>
            BuildSessionsWith<LightweightSessionFactory>();

        /// <summary>
        ///     Use identity sessions by default for the injected IDocumentSession objects. Equivalent to
        ///     IDocumentStore.IdentitySession();
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression UseIdentitySessions() =>
            BuildSessionsWith<IdentitySessionFactory>();

        /// <summary>
        ///     Use dirty-tracked sessions by default for the injected IDocumentSession objects. Equivalent to
        ///     IDocumentStore.DirtyTrackedSession();
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression UseDirtyTrackedSessions() =>
            BuildSessionsWith<DirtyTrackedSessionFactory>();

        /// <summary>
        ///     Eagerly build the application's DocumentStore during application
        ///     bootstrapping rather than waiting for the first usage of IDocumentStore
        ///     at runtime.
        /// </summary>
        /// <returns></returns>
        [Obsolete(
            "Please prefer the InitializeWith() approach for applying start up actions to a DocumentStore. This should not be used in combination with the asynchronous projections. WILL BE REMOVED IN MARTEN V6.")]
        public IDocumentStore InitializeStore()
        {
            if (_options == null)
            {
                throw new InvalidOperationException(
                    "This operation is not valid when the StoreOptions is built by Func<IServiceProvider, StoreOptions>");
            }

            var store = new DocumentStore(_options);
            Services.AddSingleton<IDocumentStore>(store);

            return store;
        }

        /// <summary>
        ///     Adds the optimized artifact workflow to this store. TODO -- LINK TO DOCS
        /// </summary>
        /// <returns></returns>
        public MartenConfigurationExpression OptimizeArtifactWorkflow() =>
            OptimizeArtifactWorkflow(TypeLoadMode.Auto);

        /// <summary>
        ///     Adds the optimized artifact workflow to this store with ability to override the TypeLoadMode in "Production" mode.
        ///     TODO -- LINK TO DOCS
        /// </summary>
        /// <param name="typeLoadMode"></param>
        /// <returns></returns>
        public MartenConfigurationExpression OptimizeArtifactWorkflow(TypeLoadMode typeLoadMode)
        {
            var configure = new OptimizedArtifactsWorkflow(typeLoadMode);
            Services.AddSingleton<IConfigureMarten>(configure);

            return this;
        }

        /// <summary>
        ///     Adds the optimized artifact workflow to this store. TODO -- LINK TO DOCS
        /// </summary>
        /// <param name="developmentEnvironment"></param>
        /// <returns></returns>
        public MartenConfigurationExpression OptimizeArtifactWorkflow(string developmentEnvironment) =>
            OptimizeArtifactWorkflow(TypeLoadMode.Auto, developmentEnvironment);

        /// <summary>
        ///     Adds the optimized artifact workflow to this store with ability to override the TypeLoadMode in "Production" mode.
        ///     TODO -- LINK TO DOCS
        /// </summary>
        /// <param name="typeLoadMode"></param>
        /// <param name="developmentEnvironment"></param>
        /// <returns></returns>
        public MartenConfigurationExpression OptimizeArtifactWorkflow(TypeLoadMode typeLoadMode,
            string developmentEnvironment)
        {
            var configure = new OptimizedArtifactsWorkflow(typeLoadMode, developmentEnvironment);
            Services.AddSingleton<IConfigureMarten>(configure);

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

internal class OptimizedArtifactsWorkflow: IConfigureMarten
{
    private readonly string _developmentEnvironment = "Development";
    private readonly TypeLoadMode _productionMode;

    public OptimizedArtifactsWorkflow(TypeLoadMode productionMode)
    {
        _productionMode = productionMode;
    }

    public OptimizedArtifactsWorkflow(TypeLoadMode productionMode, string developmentEnvironment): this(productionMode)
    {
        _developmentEnvironment = developmentEnvironment;
    }

    public void Configure(IServiceProvider services, StoreOptions options)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();

        if (environment.IsEnvironment(_developmentEnvironment))
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

internal class OptimizedArtifactsWorkflow<T>: OptimizedArtifactsWorkflow, IConfigureMarten<T>
    where T : IDocumentStore
{
    public OptimizedArtifactsWorkflow(TypeLoadMode productionMode): base(productionMode)
    {
    }

    public OptimizedArtifactsWorkflow(TypeLoadMode productionMode, string developmentEnvironment): base(productionMode,
        developmentEnvironment)
    {
    }
}
