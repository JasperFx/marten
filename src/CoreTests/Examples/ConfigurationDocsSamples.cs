using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events.EventModeling;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CoreTests.Examples;

// Samples for the configuration pages (storeoptions.md, hostbuilder.md, schema/migrations.md,
// configuration/multitenancy.md, otel.md). They compile, but are not tests.
public static class ConfigurationDocsSamples
{
    public static void auto_register_with_defaults()
    {
        #region sample_auto_register_with_defaults

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Scans the application assembly for types decorated with
            // [MartenDocument], [MartenEvent] or any other MartenAttribute
            // that overrides Register()
            opts.AutoRegister();
        });

        #endregion
    }

    public static void auto_register_with_scanner()
    {
        #region sample_auto_register_with_scanner

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Only needed when Assembly.GetEntryAssembly() is not your
            // application, a test runner for example
            opts.ApplicationAssembly = typeof(ConfigDocsOrderPlaced).Assembly;

            opts.AutoRegister(x =>
            {
                // Scan this assembly too, in addition to the application assembly
                x.Assembly(typeof(ConfigDocsCustomer).Assembly);

                // Every public, concrete type that implements or inherits
                // this type is registered as an event type
                x.EventsImplement<IConfigDocsDomainEvent>();

                // Or decide for yourself
                x.EventsMatch(type => type.Name.EndsWith("Happened"));
            });
        });

        #endregion
    }

    public static void restoring_earlier_defaults()
    {
        #region sample_restoring_earlier_defaults

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Put back every default that Marten 10 changed...
            opts.RestoreV9Defaults();

            // ...and, to go back two majors, the ones Marten 9 changed
            opts.RestoreV8Defaults();

            // Your own overrides go after the restore calls
            opts.Events.EnableExtendedProgressionTracking = true;
        });

        #endregion
    }

    public static void configure_npgsql_data_source_builder()
    {
        #region sample_configure_npgsql_data_source_builder

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Applied to every NpgsqlDataSourceBuilder Marten creates, including
            // the ones for tenant databases. This is where Npgsql plugins such as
            // UseVector(), UseNodaTime() or UseNetTopologySuite() are registered
            opts.ConfigureNpgsqlDataSourceBuilder(builder =>
            {
                builder.ConnectionStringBuilder.ApplicationName = "billing-service";
            });
        });

        #endregion
    }

    public static void less_common_store_options()
    {
        #region sample_less_common_store_options

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Lower-cased, and cut to PostgreSQL's 63 character identifier limit
            opts.DatabaseSchemaName = "Billing";

            // Names the store in monitoring and diagnostics. Defaults to "Main"
            opts.StoreName = "Billing";

            // The Event Model this store's projections contribute their slices to
            opts.EventModelName = "Billing";

            // How many operations SaveChangesAsync() sends to PostgreSQL in one
            // batched command. Defaults to 500
            opts.UpdateBatchSize = 250;

            // How long ApplyAllDatabaseChangesOnStartup() waits for the migration
            // advisory lock. Defaults to 10 seconds
            opts.ApplyChangesLockTimeout = 30.Seconds();

            // Applied to the serializer now, and again to any serializer that
            // replaces it later in the configuration
            opts.Advanced.ModifySerializer(serializer =>
            {
                if (serializer is SystemTextJsonSerializer stj)
                {
                    stj.Configure(o => o.Converters.Add(new JsonStringEnumConverter()));
                }
            });
        });

        #endregion
    }

    public static void startup_migration_failure_mode(IServiceCollection services)
    {
        #region sample_startup_migration_failure_mode

        services.AddMarten(opts =>
            {
                opts.Connection("some connection string");

                // Log a failed startup migration and keep starting, rather than
                // failing the host. Defaults to the JasperFx profile's value,
                // which is FailFast unless you change it
                opts.ResourceMigrationFailureMode = ResourceMigrationFailureMode.ContinueOnFailures;

                // Give a replica longer to win the migration lock from its peers
                opts.ApplyChangesLockTimeout = 30.Seconds();
            })
            .ApplyAllDatabaseChangesOnStartup();

        #endregion
    }

    public static void startup_migration_failure_mode_by_profile(IServiceCollection services)
    {
        #region sample_startup_migration_failure_mode_by_profile

        services.AddMarten(opts => opts.Connection("some connection string"))
            .ApplyAllDatabaseChangesOnStartup();

        // Or for every Critter Stack tool in the application, by environment
        services.CritterStackDefaults(x =>
        {
            x.Production.ResourceMigrationFailureMode = ResourceMigrationFailureMode.ContinueOnFailures;
        });

        #endregion
    }

    public static void naming_the_event_model(IServiceCollection services)
    {
        #region sample_naming_the_marten_event_model

        services.AddMarten(opts =>
        {
            opts.Connection("some connection string");

            // Contribute this store's projections to the "Billing" model
            opts.EventModelName = "Billing";
        });

        // The application's own declarations for the same model. The two
        // merge by name, and the order of these calls does not matter
        services.AddEventModel("Billing", model =>
        {
            // declared slices
        });

        #endregion
    }

    public static void multi_host_write_preference(IServiceCollection services)
    {
        #region sample_multi_host_write_session_preference

        services.AddMultiHostNpgsqlDataSource("some connection string");

        services.AddMarten(x =>
            {
                // Queries from IQuerySession may go to a standby
                x.Advanced.MultiHostSettings.ReadSessionPreference = TargetSessionAttributes.PreferStandby;

                // Everything else must reach the primary
                x.Advanced.MultiHostSettings.WriteSessionPreference = TargetSessionAttributes.Primary;
            })
            .UseNpgsqlDataSource();

        #endregion
    }

    public static void use_npgsql_data_source_with_builder_factory(IServiceCollection services)
    {
        #region sample_use_npgsql_data_source_with_builder_factory

        services.AddNpgsqlDataSource("some connection string");

        services.AddMarten()
            .UseLightweightSessions()

            // Marten uses the registered NpgsqlDataSource for its own
            // connection string, and builds data sources for any other
            // connection string (a tenant database, say) from this factory
            .UseNpgsqlDataSource(connectionString =>
            {
                var builder = new NpgsqlDataSourceBuilder(connectionString);
                builder.ConnectionStringBuilder.ApplicationName = "billing-service";
                return builder;
            });

        #endregion
    }

    public static IReadOnlyList<string> all_document_stores(IServiceProvider services)
    {
        #region sample_all_document_stores

        // The main store from AddMarten(), if there is one, followed by
        // every ancillary store from AddMartenStore<T>()
        IReadOnlyList<IDocumentStore> stores = services.AllDocumentStores();

        #endregion

        return stores.Select(x => x.GetType().Name).ToList();
    }

    public static async Task master_table_tenant_lifecycle(IDocumentStore store, CancellationToken token)
    {
        #region sample_master_table_tenant_lifecycle

        var tenancy = (MasterTableTenancy)store.Options.Tenancy;

        // Add a tenant, or change its connection string
        await tenancy.AddDatabaseRecordAsync("tenant4", "tenant4 connection string", token);

        // Switch a tenant off without losing its record. Sessions for it
        // are refused with DisabledTenantException until it is enabled again
        await tenancy.DisableTenantAsync("tenant4", token);
        IReadOnlyList<string> disabled = await tenancy.AllDisabledAsync(token);
        await tenancy.EnableTenantAsync("tenant4", token);

        // Delete a tenant's record. Its database is left alone
        await tenancy.DeleteDatabaseRecordAsync("tenant4", token);

        // Delete every tenant record, handy between integration tests
        await tenancy.ClearAllDatabaseRecordsAsync(token);

        #endregion
    }

    public static void expose_dynamic_tenant_source(IServiceCollection services)
    {
        #region sample_expose_dynamic_tenant_source

        services.AddMarten(sp =>
            {
                var options = new StoreOptions();
                options.MultiTenantedDatabasesWithMasterDatabaseTable(x =>
                {
                    x.ConnectionString = "master database connection string";
                });

                return options;
            })
            // The tenancy is only built from the function above when the store is,
            // so ask for IDynamicTenantSource<string> to be registered
            .ExposeDynamicTenantSource();

        #endregion
    }

    public static async Task master_table_tenant_lifecycle_from_services(IServiceProvider services)
    {
        #region sample_master_table_tenant_lifecycle_from_services

        // AddMarten(opts => ...) registers this when the tenancy it
        // configures is dynamic, master table tenancy included
        var tenants = services.GetRequiredService<IDynamicTenantSource<string>>();

        await tenants.AddTenantAsync("tenant5", "tenant5 connection string");
        await tenants.DisableTenantAsync("tenant5");
        await tenants.EnableTenantAsync("tenant5");

        // Deletes the tenant's record, not its database
        await tenants.RemoveTenantAsync("tenant5");

        #endregion
    }

    public static void export_counter_on_change_sets()
    {
        #region sample_export_counter_on_change_sets

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // A counter on Marten's "Marten" meter, recorded after every
            // successful SaveChangesAsync()
            opts.OpenTelemetry.ExportCounterOnChangeSets<long>(
                "billing.invoices.inserted",
                "invoices",
                (counter, commit) =>
                {
                    var invoices = commit.Inserted.OfType<ConfigDocsInvoice>().Count();
                    if (invoices > 0)
                    {
                        counter.Add(invoices);
                    }
                });
        });

        #endregion
    }
}

#region sample_marten_document_and_event_attributes

// Registered as a document type by AutoRegister()
[MartenDocument]
public class ConfigDocsCustomer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

// Registered as an event type by AutoRegister()
[MartenEvent]
public record ConfigDocsCustomerRenamed(Guid CustomerId, string Name);

// Registered as an event type, stored under the alias "customer_closed"
[MartenEvent(Alias = "customer_closed")]
public record ConfigDocsCustomerClosed(Guid CustomerId);

#endregion

public interface IConfigDocsDomainEvent
{
}

public record ConfigDocsOrderPlaced(Guid OrderId): IConfigDocsDomainEvent;

public record ConfigDocsPaymentHappened(Guid OrderId, decimal Amount);

public class ConfigDocsInvoice
{
    public Guid Id { get; set; }
}
