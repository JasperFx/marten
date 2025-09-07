using System;
using System.Linq;
using System.Text.Json;
using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Connections;
using Xunit;

namespace CoreTests;

public class StoreOptionsTests
{
    [Fact]
    public void sticky_connections_are_off_by_default()
    {
        new StoreOptions()
            .UseStickyConnectionLifetimes.ShouldBeFalse();
    }

    [Fact]
    public void STJ_is_the_default_serializer()
    {
        new StoreOptions().Serializer().ShouldBeOfType<SystemTextJsonSerializer>();
    }

    [Fact]
    public void DefaultAutoCreateShouldBeCreateOrUpdate()
    {
        var settings = new StoreOptions();

        Assert.Equal(AutoCreate.CreateOrUpdate, settings.AutoCreateSchemaObjects);
    }

    [Fact]
    public void DefaultAutoCreateShouldBeCreateOrUpdateWhenProvidingNoConfig()
    {
        var store = DocumentStore.For(ConnectionSource.ConnectionString);

        Assert.Equal(AutoCreate.CreateOrUpdate, store.Options.AutoCreateSchemaObjects);
    }


    [Fact(Skip = "sample usage code")]
    public void using_auto_create_field()
    {
        #region sample_AutoCreateSchemaObjects

        var store = DocumentStore.For(opts =>
        {
            // Marten will create any new objects that are missing,
            // attempt to update tables if it can, but drop and replace
            // tables that it cannot patch.
            opts.AutoCreateSchemaObjects = AutoCreate.All;


            // Marten will create any new objects that are missing or
            // attempt to update tables if it can. Will *never* drop
            // any existing objects, so no data loss
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;


            // Marten will create missing objects on demand, but
            // will not change any existing schema objects
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

            // Marten will not create or update any schema objects
            // and throws an exception in the case of a schema object
            // not reflecting the Marten configuration
            opts.AutoCreateSchemaObjects = AutoCreate.None;
        });

        #endregion
    }


    [Fact]
    public void CannotBuildStoreWithoutConnection()
    {
        var e = Assert.Throws<InvalidOperationException>(() => DocumentStore.For(_ => { }));

        Assert.Contains("No tenancy is configured", e.Message);
    }

    [Fact]
    public void add_document_types()
    {
        using var store = DocumentStore.For(options =>
        {
            options.Connection(ConnectionSource.ConnectionString);
            options.RegisterDocumentType<User>();
            options.RegisterDocumentType(typeof(Company));
            options.RegisterDocumentTypes(new[] { typeof(Target), typeof(Issue) });
        });

        store.Options.Storage.AllDocumentMappings.OrderBy(x => x.DocumentType.Name)
            .Select(x => x.DocumentType.Name)
            .ShouldBe(["Company", "Issue", "Target", "User"]);
    }

    [Fact]
    public void default_logger_is_the_nullo()
    {
        var options = new StoreOptions();
        options.Logger().ShouldBeOfType<NulloMartenLogger>();

        options.Logger(null);

        // doesn't matter, nullo is the default
        options.Logger().ShouldBeOfType<NulloMartenLogger>();
    }

    [Fact]
    public void can_overwrite_the_logger()
    {
        var logger = new ConsoleMartenLogger();

        var options = new StoreOptions();
        options.Logger(logger);

        options.Logger().ShouldBeSameAs(logger);
    }

    public class RecordingLogger: IMartenSessionLogger
    {
        public NpgsqlCommand LastCommand;
        public Exception LastException;

        public int OnBeforeExecuted { get; set; }

        public void LogFailure(Exception ex, string message)
        {

        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {
            OnBeforeExecuted++;
        }

        public void OnBeforeExecute(NpgsqlBatch batch)
        {

        }

        public void LogSuccess(NpgsqlCommand command)
        {
            LastCommand = command;
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
            LastCommand = command;
            LastException = ex;
        }

        public void LogSuccess(NpgsqlBatch batch)
        {

        }

        public void LogFailure(NpgsqlBatch batch, Exception ex)
        {

        }
    }

    public void using_console_logger()
    {
        #region sample_plugging-in-marten-logger

        var store = DocumentStore.For(_ =>
        {
            _.Logger(new ConsoleMartenLogger());
        });

        #endregion

        #region sample_plugging-in-session-logger

        using var session = store.LightweightSession();
        // Replace the logger for only this one session
        session.Logger = new RecordingLogger();

        #endregion
    }

    [Fact]
    public void single_tenancy_by_default()
    {
        var store = DocumentStore.For(_ =>
        {
            _.Connection(ConnectionSource.ConnectionString);
        });

        store.Tenancy.ShouldBeOfType<DefaultTenancy>();
    }

    [Fact]
    public void default_ddl_rules()
    {
        var options = new StoreOptions();

        options.Advanced.Migrator.TableCreation.ShouldBe(CreationStyle.DropThenCreate);
        options.Advanced.Migrator.UpsertRights.ShouldBe(SecurityRights.Invoker);
    }

    [Fact]
    public void ensure_patch_system_transform_functions_and_feature_schemas_are_added_only_once()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

        var store1 = new DocumentStore(options);
        // pass with the same options and check it does not throw ArgumentException
        // "An item with the same key has already been added. Key: <transform function name/feature schema name>"
        Should.NotThrow(() =>
        {
            var store2 = new DocumentStore(options);
        });
    }


    [Fact]
    public void default_enum_storage_should_be_integer()
    {
        var storeOptions = new StoreOptions();

        storeOptions.EnumStorage.ShouldBe(EnumStorage.AsInteger);
    }

    [Fact]
    public void default_duplicated_field_enum_storage_should_be_the_same_as_enum_storage()
    {
        var storeOptions = new StoreOptions();

        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);
    }

    [Theory]
    [InlineData(EnumStorage.AsInteger)]
    [InlineData(EnumStorage.AsString)]
    public void duplicated_field_enum_storage_should_be_the_same_as_enum_storage(EnumStorage enumStorage)
    {
        var storeOptions = new StoreOptions();
        storeOptions.UseSystemTextJsonForSerialization(enumStorage);

        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);
    }

    [Fact]
    public void duplicated_field_enum_storage_should_be_the_same_as_enum_storage_when_enum_storage_was_updated()
    {
        var storeOptions = new StoreOptions();
        storeOptions.UseSystemTextJsonForSerialization(EnumStorage.AsInteger);

        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);

        //update EnumStorage
        storeOptions.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        storeOptions.EnumStorage.ShouldBe(EnumStorage.AsString);
        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);
    }

    [Fact]
    public void enum_storage_should_not_change_when_duplicated_field_enum_storage_was_changed()
    {
        var storeOptions = new StoreOptions();
        storeOptions.UseSystemTextJsonForSerialization(EnumStorage.AsInteger);

        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);

        //set DuplicatedFieldEnumStorage
        storeOptions.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsString;

        storeOptions.EnumStorage.ShouldBe(EnumStorage.AsInteger);
        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(EnumStorage.AsString);
    }

    [Fact]
    public void
        duplicated_field_enum_storage_after_it_had_value_assigned_should_not_change_when_enum_storage_was_updated()
    {
        var storeOptions = new StoreOptions();
        storeOptions.UseSystemTextJsonForSerialization(EnumStorage.AsInteger);

        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);

        //set DuplicatedFieldEnumStorage
        storeOptions.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsInteger;

        //update EnumStorage
        storeOptions.UseSystemTextJsonForSerialization(EnumStorage.AsString);

        storeOptions.EnumStorage.ShouldBe(EnumStorage.AsString);
        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldNotBe(storeOptions.EnumStorage);
        storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(EnumStorage.AsInteger);
    }

    [Fact]
    public void default_code_generation_is_dynamic()
    {
        var storeOptions = new StoreOptions();
        storeOptions.GeneratedCodeMode.ShouldBe(TypeLoadMode.Dynamic);
    }

    public void set_the_maximum_name_length()
    {
        #region sample_setting-name-data-length

        var store = DocumentStore.For(_ =>
        {
            // If you have overridden NAMEDATALEN in your
            // Postgresql database to 100
            _.NameDataLength = 100;
        });

        #endregion
    }

    [Fact]
    public void SettingConnectionString_ShouldSetupDefaultNpgsqlDataSourceFactory()
    {
        // Given
        // When
        using var store = DocumentStore.For(ConnectionSource.ConnectionString);

        // Then
        store.Options.NpgsqlDataSourceFactory.ShouldBeOfType<DefaultNpgsqlDataSourceFactory>();
    }

    [Fact]
    public void SettingConnectionDataSource_ShouldRespectCurrentTenancySettings()
    {
        // Given
        var options = new StoreOptions();
        options.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString);

        // When
        options.Connection(new NpgsqlDataSourceBuilder(ConnectionSource.ConnectionString).Build());

        // Then
        options.NpgsqlDataSourceFactory.ShouldBeOfType<SingleNpgsqlDataSourceFactory>();
        options.Tenancy.ShouldBeOfType<SingleServerMultiTenancy>();
    }

    [Fact]
    public void SettingCustomDataSourceFactory_ShouldRespectDefaultTenancySettings()
    {
        // Given
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

        // When
        options.DataSourceFactory(new DummyNpgsqlDataSourceFactory());

        // Then
        options.NpgsqlDataSourceFactory.ShouldBeOfType<DummyNpgsqlDataSourceFactory>();
        options.Tenancy.ShouldBeOfType<DefaultTenancy>();
    }

    [Fact]
    public void SettingCustomDataSourceFactory_ShouldRespectCurrentTenancySettings()
    {
        // Given
        var options = new StoreOptions();
        options.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString);

        // When
        options.DataSourceFactory(new DummyNpgsqlDataSourceFactory());

        // Then
        options.NpgsqlDataSourceFactory.ShouldBeOfType<DummyNpgsqlDataSourceFactory>();
        options.Tenancy.ShouldBeOfType<SingleServerMultiTenancy>();
    }

    [Fact]
    public void SettingCustomDataSourceFactory_ShouldSetTenancyIfItsNotDefinedYet()
    {
        // Given
        var options = new StoreOptions();

        // When
        options.DataSourceFactory(new DummyNpgsqlDataSourceFactory(), ConnectionSource.ConnectionString);

        // Then
        options.NpgsqlDataSourceFactory.ShouldBeOfType<DummyNpgsqlDataSourceFactory>();
        options.Tenancy.ShouldBeOfType<DefaultTenancy>();
    }

    [InlineData(true)]
    [InlineData(false)]
    [Theory]
    public void use_base_system_text_json_serialization_options(bool indented)
    {
        // Given
        var options = new StoreOptions();

        // When
        options.UseSystemTextJsonForSerialization(new JsonSerializerOptions
        {
            WriteIndented = indented,
        });

        // Then
        var json = options.Serializer().ToJson(new
        {
            Field1 = 10,
            Field2 = 20,
        });

        if (indented)
        {
            json.ShouldContain('\n');
        }
        else
        {
            json.ShouldNotContain('\n');
        }
    }

    [Fact]
    public void default_tenant_id_style_is_case_sensitive()
    {
        new StoreOptions().TenantIdStyle.ShouldBe(TenantIdStyle.CaseSensitive);
    }

    [Fact]
    public void can_generate_options_description()
    {
        // just a smoke test
        var description = new OptionsDescription(new StoreOptions());
    }


    private class DummyNpgsqlDataSourceFactory: INpgsqlDataSourceFactory
    {
        public NpgsqlDataSource Create(string connectionString) =>
            new NpgsqlDataSourceBuilder(connectionString).Build();
    }
}
