using System;
using Baseline;
using Marten.Schema;
using Marten.Testing.Documents;
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

        [SoftDeleted]
        public class SoftDeletedDoc
        {
            public Guid Id;
        }

        [Fact]
        public void can_be_configured_by_attribute()
        {
            DocumentMapping.For<SoftDeletedDoc>()
                .DeleteStyle.ShouldBe(DeleteStyle.SoftDelete);
        }

        [Fact]
        public void can_configure_deletion_style_by_fluent_interface()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<User>().SoftDeleted();
            }))
            {
                store.Schema.MappingFor(typeof(User)).As<DocumentMapping>()
                    .DeleteStyle.ShouldBe(DeleteStyle.SoftDelete);
            }
        }
    }
}