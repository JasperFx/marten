using System;
using System.Linq;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing
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
        public void PLV8Enabled_is_true_by_default()
        {
            new StoreOptions().PLV8Enabled.ShouldBeTrue();
        }

        [Fact]
        public void add_document_types()
        {
            var options = new StoreOptions();
            options.RegisterDocumentType<User>();
            options.RegisterDocumentType(typeof(Company));
            options.RegisterDocumentTypes(new Type[] {typeof(Target), typeof(Issue)});

            options.Storage.AllDocumentMappings.OrderBy(x => x.DocumentType.Name).Select(x => x.DocumentType.Name)
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
            // SAMPLE: plugging-in-marten-logger
            var store = DocumentStore.For(_ =>
            {
                _.Logger(new ConsoleMartenLogger());
            });
            // ENDSAMPLE

            // SAMPLE: plugging-in-session-logger
            using (var session = store.OpenSession())
            {
                // Replace the logger for only this one session
                session.Logger = new RecordingLogger();
            }
            // ENDSAMPLE
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

            options.DdlRules.TableCreation.ShouldBe(CreationStyle.DropThenCreate);
            options.DdlRules.UpsertRights.ShouldBe(SecurityRights.Invoker);
        }

        public class FakeUserStorage : IDocumentStorage, IdAssignment<User>
        {
            public TenancyStyle TenancyStyle { get; }
            public Type DocumentType { get; } = typeof (User);
            public Type TopLevelBaseType => DocumentType;
            public NpgsqlDbType IdType { get; }
            public NpgsqlCommand LoaderCommand(object id)
            {
                throw new NotImplementedException();
            }

            public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
            {
                throw new NotImplementedException();
            }

            public object Identity(object document)
            {
                throw new NotImplementedException();
            }

            public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity)
            {
                throw new NotImplementedException();
            }

            public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, string json)
            {
                throw new NotImplementedException();
            }

            public void Remove(IIdentityMap map, object entity)
            {
                throw new NotImplementedException();
            }

            public void Delete(IIdentityMap map, object id)
            {
                throw new NotImplementedException();
            }

            public void Store(IIdentityMap map, object id, object entity)
            {
                throw new NotImplementedException();
            }

            public IStorageOperation DeletionForId(object id)
            {
                throw new NotImplementedException();
            }

            public IStorageOperation DeletionForEntity(object entity)
            {
                throw new NotImplementedException();
            }

            public IStorageOperation DeletionForWhere(IWhereFragment @where)
            {
                throw new NotImplementedException();
            }

            public object Assign(ITenant tenant, User document, out bool assigned)
            {
                throw new NotImplementedException();
            }

            public void Assign(ITenant tenant, User document, object id)
            {
                document.Id = (Guid) id;
            }
        }

        public class FakeCompanyStorage : IDocumentStorage, IdAssignment<Company>
        {
            public TenancyStyle TenancyStyle { get; }
            public Type DocumentType { get; } = typeof (Company);
            public Type TopLevelBaseType { get; } = typeof(Company);
            public NpgsqlDbType IdType { get; }
            public NpgsqlCommand LoaderCommand(object id)
            {
                throw new NotImplementedException();
            }


            public NpgsqlCommand LoadByArrayCommand<TKey>(TKey[] ids)
            {
                throw new NotImplementedException();
            }

            public object Identity(object document)
            {
                throw new NotImplementedException();
            }

            public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity)
            {
                throw new NotImplementedException();
            }

            public void RegisterUpdate(string tenantIdOverride, UpdateStyle updateStyle, UpdateBatch batch, object entity, string json)
            {
                throw new NotImplementedException();
            }

            public void Remove(IIdentityMap map, object entity)
            {
                throw new NotImplementedException();
            }

            public void Delete(IIdentityMap map, object id)
            {
                throw new NotImplementedException();
            }

            public void Store(IIdentityMap map, object id, object entity)
            {
                throw new NotImplementedException();
            }

            public IStorageOperation DeletionForId(object id)
            {
                throw new NotImplementedException();
            }

            public IStorageOperation DeletionForEntity(object entity)
            {
                throw new NotImplementedException();
            }

            public IStorageOperation DeletionForWhere(IWhereFragment @where)
            {
                throw new NotImplementedException();
            }

            public object Assign(ITenant tenant, Company document, out bool assigned)
            {
                throw new NotImplementedException();
            }

            public void Assign(ITenant tenant, Company document, object id)
            {
                document.Id = (Guid) id;
            }
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

        public void set_the_maximum_name_length()
        {
            // SAMPLE: setting-name-data-length
            var store = DocumentStore.For(_ =>
            {
                // If you have overridden NAMEDATALEN in your
                // Postgresql database to 100
                _.NameDataLength = 100;
            });
            // ENDSAMPLE
        }
    }
}