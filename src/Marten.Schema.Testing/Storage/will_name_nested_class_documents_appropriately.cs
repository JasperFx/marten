using System;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    public class will_name_nested_class_documents_appropriately : IntegrationContext
    {
        [Fact]
        public void will_name_nested_class_table_with_containing_class_name_prefix()
        {
            DocumentTable table1;
            DocumentTable table2;


            theStore.Tenancy.Default.StorageFor(typeof(Foo.Document));
            theStore.Tenancy.Default.StorageFor(typeof(Bar.Document));

            var documentTables = theStore.Tenancy.Default.DbObjects.DocumentTables();
            documentTables.ShouldContain("public.mt_doc_foo_document");
            documentTables.ShouldContain("public.mt_doc_bar_document");

            table1 = theStore.TableSchema(typeof(Foo.Document));
            table1.Identifier.Name.ShouldBe("mt_doc_foo_document");

            table2 = theStore.TableSchema(typeof(Bar.Document));
            table2.Identifier.Name.ShouldBe("mt_doc_bar_document");



            table2.Equals(table1).ShouldBeFalse();
        }

        [Fact]
        public void will_name_nested_class_table_with_containing_class_name_prefix_on_other_database_schema()
        {
            DocumentTable table1;
            DocumentTable table2;

            StoreOptions(x => x.DatabaseSchemaName = "other");

                theStore.Tenancy.Default.StorageFor(typeof(Foo.Document));
                theStore.Tenancy.Default.StorageFor(typeof(Bar.Document));

                var documentTables = theStore.Tenancy.Default.DbObjects.DocumentTables();
                documentTables.ShouldContain("other.mt_doc_foo_document");
                documentTables.ShouldContain("other.mt_doc_bar_document");

                table1 = theStore.TableSchema(typeof(Foo.Document));
                table1.Identifier.Name.ShouldBe("mt_doc_foo_document");

                table2 = theStore.TableSchema(typeof(Bar.Document));
                table2.Identifier.Name.ShouldBe("mt_doc_bar_document");

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
