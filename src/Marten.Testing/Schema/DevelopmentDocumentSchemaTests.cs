using System;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing.Schema
{
    public class DevelopmentDocumentSchemaTests : IDisposable
    {
        private readonly DevelopmentDocumentSchema _schema = new DevelopmentDocumentSchema(new ConnectionSource());

        public DevelopmentDocumentSchemaTests()
        {
            ConnectionSource.CleanBasicDocuments();
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
                .ShouldBeSameAs(_schema.StorageFor(typeof(User)));

            _schema.StorageFor(typeof(Issue))
                .ShouldBeSameAs(_schema.StorageFor(typeof(Issue)));

            _schema.StorageFor(typeof(Company))
                .ShouldBeSameAs(_schema.StorageFor(typeof(Company)));
        }  

        public void builds_schema_objects_on_the_fly_as_needed()
        {
            _schema.StorageFor(typeof (User)).ShouldNotBeNull();
            _schema.StorageFor(typeof (Issue)).ShouldNotBeNull();
            _schema.StorageFor(typeof (Company)).ShouldNotBeNull();

            var runner = new CommandRunner(new ConnectionSource());
                var tables = runner.SchemaTableNames();
                tables.ShouldContain(SchemaBuilder.TableNameFor(typeof(User)).ToLower());
                tables.ShouldContain(SchemaBuilder.TableNameFor(typeof(Issue)).ToLower());
                tables.ShouldContain(SchemaBuilder.TableNameFor(typeof(Company)).ToLower());

                var functions = runner.SchemaFunctionNames();
                functions.ShouldContain(SchemaBuilder.UpsertNameFor(typeof(User)).ToLower());
                functions.ShouldContain(SchemaBuilder.UpsertNameFor(typeof(Issue)).ToLower());
                functions.ShouldContain(SchemaBuilder.UpsertNameFor(typeof(Company)).ToLower());
        }
    }
}