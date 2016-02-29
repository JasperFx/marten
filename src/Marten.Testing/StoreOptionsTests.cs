using System;
using System.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class StoreOptionsTests
    {
        [Fact]
        public void add_document_types()
        {
            var options = new StoreOptions();
            options.RegisterDocumentType<User>();
            options.RegisterDocumentType(typeof(Company));
            options.RegisterDocumentTypes(new Type[] {typeof(Target), typeof(Issue)});

            options.AllDocumentMappings.OrderBy(x => x.DocumentType.Name).Select(x => x.DocumentType.Name)
                .ShouldHaveTheSameElementsAs("Company", "Issue", "Target", "User");
        }

        [Fact]
        public void import_document_storage_from_assembly()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.LoadPrecompiledStorageFrom(GetType().Assembly);
                _.AutoCreateSchemaObjects = true;
            }))
            {
                store.Schema.StorageFor(typeof (User)).ShouldBeOfType<FakeUserStorage>();
                store.Schema.StorageFor(typeof (Company)).ShouldBeOfType<FakeCompanyStorage>();
            }
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

        public class FakeUserStorage : IDocumentStorage, IdAssignment<User>
        {
            public Type DocumentType { get; } = typeof (User);
            public NpgsqlDbType IdType { get; }
            public NpgsqlCommand LoaderCommand(object id)
            {
                throw new NotImplementedException();
            }

            public NpgsqlCommand DeleteCommandForId(object id)
            {
                throw new NotImplementedException();
            }

            public NpgsqlCommand DeleteCommandForEntity(object entity)
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

            public void RegisterUpdate(UpdateBatch batch, object entity)
            {
                throw new NotImplementedException();
            }

            public void RegisterUpdate(UpdateBatch batch, object entity, string json)
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

            public object Assign(User document, out bool assigned)
            {
                throw new NotImplementedException();
            }
        }

        public class FakeCompanyStorage : IDocumentStorage, IdAssignment<Company>
        {
            public Type DocumentType { get; } = typeof (Company);
            public NpgsqlDbType IdType { get; }
            public NpgsqlCommand LoaderCommand(object id)
            {
                throw new NotImplementedException();
            }

            public NpgsqlCommand DeleteCommandForId(object id)
            {
                throw new NotImplementedException();
            }

            public NpgsqlCommand DeleteCommandForEntity(object entity)
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

            public void RegisterUpdate(UpdateBatch batch, object entity)
            {
                throw new NotImplementedException();
            }

            public void RegisterUpdate(UpdateBatch batch, object entity, string json)
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

            public object Assign(Company document, out bool assigned)
            {
                throw new NotImplementedException();
            }
        }
    }
}