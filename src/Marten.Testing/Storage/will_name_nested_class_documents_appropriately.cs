using System;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Testing.Storage
{
    public class will_name_nested_class_documents_appropriately
    {
        [Fact]
        public void will_name_nested_class_table_with_containing_class_name_prefix()
        {
            DocumentTable table1;
            DocumentTable table2;

            using (var store = TestingDocumentStore.Basic())
            {
                store.Tenancy.Default.StorageFor(typeof(Foo.Document));
                store.Tenancy.Default.StorageFor(typeof(Bar.Document));

                var documentTables = store.Tenancy.Default.DbObjects.DocumentTables();
                documentTables.ShouldContain("public.mt_doc_foo_document");
                documentTables.ShouldContain("public.mt_doc_bar_document");

                table1 = store.TableSchema(typeof(Foo.Document));
                table1.Identifier.Name.ShouldBe("mt_doc_foo_document");

                table2 = store.TableSchema(typeof(Bar.Document));
                table2.Identifier.Name.ShouldBe("mt_doc_bar_document");
            }


            table2.Equals(table1).ShouldBeFalse();
        }

        [Fact]
        public void will_name_nested_class_table_with_containing_class_name_prefix_on_other_database_schema()
        {
            DocumentTable table1;
            DocumentTable table2;

            using (var store = TestingDocumentStore.For(_ => _.DatabaseSchemaName = "other"))
            {
                store.Tenancy.Default.StorageFor(typeof(Foo.Document));
                store.Tenancy.Default.StorageFor(typeof(Bar.Document));

                var documentTables = store.Tenancy.Default.DbObjects.DocumentTables();
                documentTables.ShouldContain("other.mt_doc_foo_document");
                documentTables.ShouldContain("other.mt_doc_bar_document");

                table1 = store.TableSchema(typeof(Foo.Document));
                table1.Identifier.Name.ShouldBe("mt_doc_foo_document");

                table2 = store.TableSchema(typeof(Bar.Document));
                table2.Identifier.Name.ShouldBe("mt_doc_bar_document");
            }


            table2.Equals(table1).ShouldBeFalse();
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
