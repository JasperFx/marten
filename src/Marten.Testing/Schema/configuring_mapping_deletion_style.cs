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

        public void example_of_using_fi_to_configure()
        {
            // SAMPLE: soft-delete-configuration-via-fi
    var store = DocumentStore.For(_ =>
    {
        _.Connection(ConnectionSource.ConnectionString);
        _.Schema.For<User>().SoftDeleted();
    });

    store.Dispose();
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
                store.Schema.MappingFor(typeof(User)).As<DocumentMapping>()
                    .DeleteStyle.ShouldBe(DeleteStyle.SoftDelete);
            }
            
        }
    }
}