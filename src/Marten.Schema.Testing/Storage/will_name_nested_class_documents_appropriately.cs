using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    public class will_name_nested_class_documents_appropriately : IntegrationContext
    {
        [Fact]
        public async Task will_name_nested_class_table_with_containing_class_name_prefix()
        {
            DocumentTable table1;
            DocumentTable table2;


            theStore.Tenancy.Default.StorageFor<Foo.Document>();
            theStore.Tenancy.Default.StorageFor<Bar.Document>();

            var documentTables = (await theStore.Tenancy.Default.DocumentTables()).Select(x => x.QualifiedName).ToArray();
            documentTables.ShouldContain("public.mt_doc_foo_document");
            documentTables.ShouldContain("public.mt_doc_bar_document");

            table1 = theStore.TableSchema(typeof(Foo.Document));
            table1.Identifier.Name.ShouldBe("mt_doc_foo_document");

            table2 = theStore.TableSchema(typeof(Bar.Document));
            table2.Identifier.Name.ShouldBe("mt_doc_bar_document");



            table2.Equals(table1).ShouldBeFalse();
        }

        [Fact]
        public async Task will_name_nested_class_table_with_containing_class_name_prefix_on_other_database_schema()
        {
            DocumentTable table1;
            DocumentTable table2;

            StoreOptions(x => x.DatabaseSchemaName = "other");

                theStore.Tenancy.Default.StorageFor<Foo.Document>();
                theStore.Tenancy.Default.StorageFor<Bar.Document>();

                var documentTables = (await theStore.Tenancy.Default.DocumentTables()).Select(x => x.QualifiedName).ToArray();
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
