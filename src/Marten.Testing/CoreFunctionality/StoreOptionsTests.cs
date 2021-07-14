using System;
using System.Linq;
using LamarCodeGeneration;
using Marten.Exceptions;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class StoreOptionsTests
    {
        [Fact]
        public void CannotBuildStoreWithoutConnection()
        {
            var e = Assert.Throws<InvalidOperationException>(() => DocumentStore.For(_ => { }));

            Assert.Contains("Tenancy not specified", e.Message);
        }

        [Fact]
        public void add_document_types()
        {
            using var store = DocumentStore.For(options =>
            {
                options.Connection(ConnectionSource.ConnectionString);
                options.RegisterDocumentType<User>();
                options.RegisterDocumentType(typeof(Company));
                options.RegisterDocumentTypes(new[] {typeof(Target), typeof(Issue)});
            });

            store.Options.Storage.AllDocumentMappings.OrderBy(x => x.DocumentType.Name)
                .Select(x => x.DocumentType.Name)
                .ShouldHaveTheSameElementsAs("Company", "Issue", "Target", "User");
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

        public void using_console_logger()
        {
            #region sample_plugging-in-marten-logger

            var store = DocumentStore.For(_ =>
            {
                _.Logger(new ConsoleMartenLogger());
            });

            #endregion sample_plugging-in-marten-logger

            #region sample_plugging-in-session-logger

            using (var session = store.OpenSession())
            {
                // Replace the logger for only this one session
                session.Logger = new RecordingLogger();
            }

            #endregion sample_plugging-in-session-logger
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

            options.Advanced.DdlRules.TableCreation.ShouldBe(CreationStyle.DropThenCreate);
            options.Advanced.DdlRules.UpsertRights.ShouldBe(SecurityRights.Invoker);
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
        public void default_name_data_length_is_64()
        {
            new StoreOptions().NameDataLength.ShouldBe(64);
        }

        [Fact]
        public void assert_identifier_length_happy_path()
        {
            var options = new StoreOptions();

            for (var i = 1; i < options.NameDataLength; i++)
            {
                var text = new string('a', i);

                options.AssertValidIdentifier(text);
            }
        }

        [Fact]
        public void assert_identifier_must_not_contain_space()
        {
            var random = new Random();
            var options = new StoreOptions();

            for (var i = 1; i < options.NameDataLength; i++)
            {
                var text = new string('a', i);
                var position = random.Next(0, i);

                Exception<PostgresqlIdentifierInvalidException>.ShouldBeThrownBy(() =>
                {
                    options.AssertValidIdentifier(text.Remove(position).Insert(position, " "));
                });
            }
        }

        [Fact]
        public void assert_identifier_null_or_whitespace()
        {
            var options = new StoreOptions();

            Exception<PostgresqlIdentifierInvalidException>.ShouldBeThrownBy(() =>
            {
                options.AssertValidIdentifier(null);
            });

            for (var i = 0; i < options.NameDataLength; i++)
            {
                var text = new string(' ', i);

                Exception<PostgresqlIdentifierInvalidException>.ShouldBeThrownBy(() =>
                {
                    options.AssertValidIdentifier(text);
                });
            }
        }

        [Fact]
        public void assert_identifier_length_exceeding_maximum()
        {
            var options = new StoreOptions();

            var text = new string('a', options.NameDataLength);

            Exception<PostgresqlIdentifierTooLongException>.ShouldBeThrownBy(() =>
            {
                options.AssertValidIdentifier(text);
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
            storeOptions.UseDefaultSerialization(enumStorage);

            storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);
        }

        [Fact]
        public void duplicated_field_enum_storage_should_be_the_same_as_enum_storage_when_enum_storage_was_updated()
        {
            var storeOptions = new StoreOptions();
            storeOptions.UseDefaultSerialization(EnumStorage.AsInteger);

            storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);

            //update EnumStorage
            storeOptions.UseDefaultSerialization(EnumStorage.AsString);

            storeOptions.EnumStorage.ShouldBe(EnumStorage.AsString);
            storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);
        }

        [Fact]
        public void enum_storage_should_not_change_when_duplicated_field_enum_storage_was_changed()
        {
            var storeOptions = new StoreOptions();
            storeOptions.UseDefaultSerialization(EnumStorage.AsInteger);

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
            storeOptions.UseDefaultSerialization(EnumStorage.AsInteger);

            storeOptions.Advanced.DuplicatedFieldEnumStorage.ShouldBe(storeOptions.EnumStorage);

            //set DuplicatedFieldEnumStorage
            storeOptions.Advanced.DuplicatedFieldEnumStorage = EnumStorage.AsInteger;

            //update EnumStorage
            storeOptions.UseDefaultSerialization(EnumStorage.AsString);

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

            #endregion sample_setting-name-data-length
        }
    }
}
