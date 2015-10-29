using System;
using System.Linq;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Schema
{
    public class DocumentSchemaTests : IDisposable
    {
        private readonly DocumentSchema _schema;
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        public DocumentSchemaTests()
        {
            ConnectionSource.CleanBasicDocuments();
            _schema = _container.GetInstance<DocumentSchema>();
        }

        public void Dispose()
        {
            _schema.Dispose();
        }

        public void can_create_a_new_storage_for_an_IDocument_type()
        {
            var storage = _schema.StorageFor(typeof (User));
            storage.ShouldNotBeNull();
            storage.ShouldBeOfType<DocumentStorage<User, Guid>>();
        }

        public void caches_storage_for_a_document_type()
        {
            _schema.StorageFor(typeof (User))
                .ShouldBeSameAs(_schema.StorageFor(typeof (User)));

            _schema.StorageFor(typeof (Issue))
                .ShouldBeSameAs(_schema.StorageFor(typeof (Issue)));

            _schema.StorageFor(typeof (Company))
                .ShouldBeSameAs(_schema.StorageFor(typeof (Company)));
        }

        public void builds_schema_objects_on_the_fly_as_needed()
        {
            _schema.StorageFor(typeof (User)).ShouldNotBeNull();
            _schema.StorageFor(typeof (Issue)).ShouldNotBeNull();
            _schema.StorageFor(typeof (Company)).ShouldNotBeNull();

            var schema = new DocumentSchema(new ConnectionSource(), new DevelopmentSchemaCreation(new ConnectionSource()));
            var tables = schema.SchemaTableNames();
            tables.ShouldContain(SchemaBuilder.TableNameFor(typeof (User)).ToLower());
            tables.ShouldContain(SchemaBuilder.TableNameFor(typeof (Issue)).ToLower());
            tables.ShouldContain(SchemaBuilder.TableNameFor(typeof (Company)).ToLower());

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain(SchemaBuilder.UpsertNameFor(typeof (User)).ToLower());
            functions.ShouldContain(SchemaBuilder.UpsertNameFor(typeof (Issue)).ToLower());
            functions.ShouldContain(SchemaBuilder.UpsertNameFor(typeof (Company)).ToLower());
        }

        public void do_not_rebuild_a_table_that_already_exists()
        {
            using (var container1 = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container1.GetInstance<IDocumentSession>())
                {
                    session.Store(new User());
                    session.Store(new User());
                    session.Store(new User());

                    session.SaveChanges();
                }
            }

            using (var container2 = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container2.GetInstance<IDocumentSession>())
                {
                    session.Query<User>().Count().ShouldBeGreaterThanOrEqualTo(3);
                }
            }

        }
    }
}