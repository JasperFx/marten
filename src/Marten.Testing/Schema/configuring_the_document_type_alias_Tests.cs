using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class configuring_the_document_type_alias_Tests
    {
        [Fact]
        public void DocumentAlias_attribute_changes_the_alias()
        {
            var mapping = DocumentMapping.For<Tractor>();

            mapping.Alias.ShouldBe("johndeere");
            mapping.Table.Name.ShouldBe("mt_doc_johndeere");
        }

        [Fact]
        public void document_alias_can_be_overridden_with_the_marten_registry()
        {
            // SAMPLE: marten-registry-to-override-document-alias
            var store = DocumentStore.For(_ =>
            {
                _.Connection("something");

                _.Schema.For<User>().DocumentAlias("folks");
            });
            // ENDSAMPLE

            store.Schema.MappingFor(typeof(User)).As<DocumentMapping>().Alias.ShouldBe("folks");
        }

        // SAMPLE: using-document-alias-attribute
        [DocumentAlias("johndeere")]
        public class Tractor
        {
            public string id;
        }
        // ENDSAMPLE
    }
}