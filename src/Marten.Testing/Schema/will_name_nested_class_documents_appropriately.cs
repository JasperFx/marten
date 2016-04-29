using System.Linq;
using Marten.Generation;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;
using Xunit;
using System;
using Marten.Schema;

namespace Marten.Testing.Schema
{
    public class will_name_nested_class_documents_appropriately
    {
        [Fact]
        public void will_name_nested_class_table_with_containing_class_name_prefix()
        {
            TableDefinition table1;
            TableDefinition table2;

            using (var container = ContainerFactory.Default())
            {
                var store = container.GetInstance<IDocumentStore>();

                store.Advanced.Clean.CompletelyRemoveAll();

                store.Schema.StorageFor(typeof (Foo.Document));
                store.Schema.StorageFor(typeof (Bar.Document));

                var documentTables = store.Schema.DbObjects.DocumentTables();
                documentTables.ShouldContain("public.mt_doc_foo_document");
                documentTables.ShouldContain("public.mt_doc_bar_document");

                table1 = store.Schema.TableSchema(typeof (Foo.Document));
                table1.Table.Name.ShouldBe("mt_doc_foo_document");

                table2 = store.Schema.TableSchema(typeof (Bar.Document));
                table2.Table.Name.ShouldBe("mt_doc_bar_document");
            }

            table2.ShouldNotBe(table1);
        }

        [Fact]
        public void will_name_nested_class_table_with_containing_class_name_prefix_on_other_database_schema()
        {
            TableDefinition table1;
            TableDefinition table2;

            using (var container = ContainerFactory.OnOtherDatabaseSchema())
            {
                var store = container.GetInstance<IDocumentStore>();

                store.Advanced.Clean.CompletelyRemoveAll();

                store.Schema.StorageFor(typeof(Foo.Document));
                store.Schema.StorageFor(typeof(Bar.Document));

                var documentTables = store.Schema.DbObjects.DocumentTables();
                documentTables.ShouldContain("other.mt_doc_foo_document");
                documentTables.ShouldContain("other.mt_doc_bar_document");

                table1 = store.Schema.TableSchema(typeof(Foo.Document));
                table1.Table.Name.ShouldBe("mt_doc_foo_document");

                table2 = store.Schema.TableSchema(typeof(Bar.Document));
                table2.Table.Name.ShouldBe("mt_doc_bar_document");
            }

            table2.ShouldNotBe(table1);
        }
    }

    public class Foo
    {
        public class Document
        {
            public Guid Id { get; set; }
        }
    }

    public class Bar
    {
        public class Document
        {
            public Guid Id { get; set; }
        }
    }
}
