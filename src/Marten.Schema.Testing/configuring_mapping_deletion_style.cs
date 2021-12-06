using System;
using System.Linq;
using Baseline;
using Marten.Schema.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing
{
    public class configuring_mapping_deletion_style
    {

        [Fact]
        public void default_delete_style_is_remove()
        {
            DocumentMapping.For<User>()
                .DeleteStyle.ShouldBe(DeleteStyle.Remove);
        }

        #region sample_SoftDeletedAttribute
        [SoftDeleted]
        public class SoftDeletedDoc
        {
            public Guid Id;
        }
        #endregion

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

        #region sample_SoftDeletedWithIndexAttribute
        [SoftDeleted(Indexed = true)]
        public class IndexedSoftDeletedDoc
        {
            public Guid Id;
        }
        #endregion

        [Fact(Skip = "sample usage code")]
        public void example_of_using_fi_to_configure()
        {
            #region sample_soft-delete-configuration-via-fi
            var store = DocumentStore.For(_ =>
            {
                _.Schema.For<User>().SoftDeleted();
            });
            #endregion

            #region sample_soft-delete-with-index-configuration-via-fi
            DocumentStore.For(_ =>
            {
                _.Schema.For<User>().SoftDeletedWithIndex();
            });
            #endregion
        }

    }
}
