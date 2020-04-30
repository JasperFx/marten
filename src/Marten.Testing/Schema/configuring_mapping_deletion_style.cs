using System;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class configuring_mapping_deletion_style
    {
        [Fact]
        public void default_delete_style_is_remove()
        {
            DocumentMapping.For<User>()
                .DeleteStyle.ShouldBe(DeleteStyle.Remove);
        }

        // SAMPLE: SoftDeletedAttribute
        [SoftDeleted]
        public class SoftDeletedDoc
        {
            public Guid Id;
        }
        // ENDSAMPLE

        [Fact]
        public void can_be_configured_by_attribute()
        {
            DocumentMapping.For<SoftDeletedDoc>()
                .DeleteStyle.ShouldBe(DeleteStyle.SoftDelete);
        }

        [Fact]
        public void can_configure_index_via_attribute()
        {
            DocumentMapping.For<IndexedSoftDeletedDoc>()
                .IndexesFor(DocumentMapping.DeletedAtColumn)
                .Count()
                .ShouldBe(1);
        }

        // SAMPLE: SoftDeletedWithIndexAttribute
        [SoftDeleted(Indexed = true)]
        public class IndexedSoftDeletedDoc
        {
            public Guid Id;
        }
        // ENDSAMPLE

        public void example_of_using_fi_to_configure()
        {
            // SAMPLE: soft-delete-configuration-via-fi
            DocumentStore.For(_ =>
            {
                _.Schema.For<User>().SoftDeleted();
            });
            // ENDSAMPLE
            // SAMPLE: soft-delete-with-index-configuration-via-fi
            DocumentStore.For(_ =>
            {
                _.Schema.For<User>().SoftDeletedWithIndex();
            });
            // ENDSAMPLE
        }

        [Fact]
        public void can_configure_deletion_style_by_fluent_interface()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<User>().SoftDeleted();
            }))
            {
                store.Storage.MappingFor(typeof(User)).As<DocumentMapping>()
                    .DeleteStyle.ShouldBe(DeleteStyle.SoftDelete);
            }
        }
    }
}
