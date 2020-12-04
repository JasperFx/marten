using System;
using System.Linq;
using Baseline;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    [Collection("soft_deletes")]
    public class configuring_mapping_deletion_style : IntegrationContext
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
                .IndexesFor(SchemaConstants.DeletedAtColumn)
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

        [Fact(Skip = "sample usage code")]
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
            using (var store = StoreOptions(_ =>
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
