using System;
using System.Collections.Generic;
using System.Text;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class assigning_versions_to_documents
    {

        [Fact]
        public void no_version_member_by_default()
        {
            DocumentMapping.For<User>().VersionMember.ShouldBeNull();
        }

        [Fact]
        public void setting_version_member_opts_into_optimistic_concurrency()
        {
            DocumentMapping.For<AttVersionedDoc>()
                .UseOptimisticConcurrency.ShouldBeTrue();
        }

        [Fact]
        public void version_member_set_by_attribute()
        {
            DocumentMapping.For<AttVersionedDoc>()
                .VersionMember.Name.ShouldBe("Version");
        }

        [Fact]
        public void wrong_version_member()
        {
            Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
            {
                DocumentMapping.For<WrongVersionTypedDoc>();
            });
        }

        [Fact]
        public void set_the_version_member_through_the_fluent_interface()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<DocThatCouldBeVersioned>().VersionedWith(x => x.Revision);
            }))
            {
                store.Storage.MappingFor(typeof(DocThatCouldBeVersioned))
                    .VersionMember.Name.ShouldBe(nameof(DocThatCouldBeVersioned.Revision));
            }
        }
    }

    public class DocThatCouldBeVersioned
    {
        public int Id;
        public Guid Revision;
    }

    public class AttVersionedDoc
    {
        public int Id;

        [Version]
        public Guid Version;
    }

    public class WrongVersionTypedDoc
    {
        public int Id;

        [Version]
        public string Version;
    }
}
