using System;
using System.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage
{
    [Collection("hierarchy")]
    public class end_to_end_document_hierarchy_with_interface_tests: OneOffConfigurationsContext
    {
        public end_to_end_document_hierarchy_with_interface_tests()
        {
            StoreOptions(
                _ =>
                {
                    _.Schema.For<IPolicy>()
                        .Identity(x => x.VersionId)
                        .AddSubClassHierarchy();

                    _.Schema.For<IPolicy>().GinIndexJsonData();
                });
        }

        [Fact]
        public void persists_subclass()
        {
            var policy = new LinuxPolicy {Name = Guid.NewGuid().ToString()};
            using (var session = theStore.LightweightSession())
            {
                session.Store(policy);
                session.SaveChanges();
            }
        }


        [Fact]
        public void query_for_only_a_subclass_with_string_where_clause()
        {
            var policy = new LinuxPolicy {VersionId = Guid.NewGuid(), Name = Guid.NewGuid().ToString()};
            using (var session = theStore.LightweightSession())
            {
                session.Store(policy);
                session.SaveChanges();
            }

            using (var session = theStore.LightweightSession())
            {
                session.Query<IPolicy>($"Where id = \'{policy.VersionId}\'").Single()
                    .VersionId.ShouldBe(policy.VersionId);
            }
        }


        [Fact]
        public void query_for_only_a_subclass_with_where_clause()
        {
            var policy = new LinuxPolicy {VersionId = Guid.NewGuid(), Name = Guid.NewGuid().ToString()};
            using (var session = theStore.LightweightSession())
            {
                session.Store(policy);
                session.SaveChanges();
            }

            using (var session = theStore.LightweightSession())
            {
                session.Query<IPolicy>().Single(p => p.VersionId == policy.VersionId)
                    .VersionId.ShouldBe(policy.VersionId);
            }
        }
    }
}
