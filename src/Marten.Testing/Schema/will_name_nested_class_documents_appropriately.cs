using System.Linq;
using Marten.Generation;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;
using Xunit;
using System;

namespace Marten.Testing.Schema
{
    public class will_name_nested_class_documents_appropriately
    {
        [Fact]
        public void will_name_nested_class_table_with_containing_class_name_prefix()
        {
            TableDefinition table1;
            TableDefinition table2;

            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();

                store.Advanced.Clean.CompletelyRemoveAll();

                store.Schema.StorageFor(typeof(Foo.Document));
                store.Schema.StorageFor(typeof(Bar.Document));

                store.Schema.DocumentTables().ShouldContain(x => x == "mt_doc_foo_document");
                store.Schema.DocumentTables().ShouldContain(x => x == "mt_doc_bar_document");

                table1 = store.Schema.TableSchema("mt_doc_foo_document");
                table2 = store.Schema.TableSchema("mt_doc_bar_document");
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
