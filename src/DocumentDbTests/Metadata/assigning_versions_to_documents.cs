using System;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata
{
    [Collection("acceptance")]
    public class assigning_versions_to_documents : OneOffConfigurationsContext
    {
        [Fact]
        public void no_version_member_by_default()
        {
            SpecificationExtensions.ShouldBeNull(DocumentMapping.For<User>().Metadata.Version.Member);
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
                .Metadata
                .Version.Member.Name.ShouldBe("Version");
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
            using (var store = SeparateStore(_ =>
            {
                _.Schema.For<DocThatCouldBeVersioned>().Metadata(m =>
                {
                    m.Version.MapTo(x => x.Revision);
                });
            }))
            {
                store.StorageFeatures.MappingFor(typeof(DocThatCouldBeVersioned))
                    .Metadata.Version.Member.Name.ShouldBe(nameof(DocThatCouldBeVersioned.Revision));
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

    public class PropVersionedDoc
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
