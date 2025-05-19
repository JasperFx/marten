using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.HierarchicalStorage;

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
    public async Task persists_subclass()
    {
        var policy = new LinuxPolicy {Name = Guid.NewGuid().ToString()};
        using (var session = theStore.LightweightSession())
        {
            session.Store(policy);
            await session.SaveChangesAsync();
        }
    }


    [Fact]
    public async Task query_for_only_a_subclass_with_string_where_clause()
    {
        var policy = new LinuxPolicy {VersionId = Guid.NewGuid(), Name = Guid.NewGuid().ToString()};
        using (var session = theStore.LightweightSession())
        {
            session.Store(policy);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            (await session.QueryAsync<IPolicy>($"Where id = \'{policy.VersionId}\'")).Single()
                .VersionId.ShouldBe(policy.VersionId);
        }
    }


    [Fact]
    public async Task query_for_only_a_subclass_with_where_clause()
    {
        var policy = new LinuxPolicy {VersionId = Guid.NewGuid(), Name = Guid.NewGuid().ToString()};
        using (var session = theStore.LightweightSession())
        {
            session.Store(policy);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            session.Query<IPolicy>().Single(p => p.VersionId == policy.VersionId)
                .VersionId.ShouldBe(policy.VersionId);
        }
    }
}
