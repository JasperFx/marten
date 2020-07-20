using System;
using System.Linq;
using Marten.Services;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Hierarchies
{
    public class end_to_end_document_hierarchy_with_interface_tests: IntegrationContext
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

    public abstract class BasePolicy: IPolicy
    {
        public Guid VersionId { get; set; } = Guid.NewGuid();
        public Guid DocumentId { get; set; } = Guid.NewGuid();

        [JsonIgnore] public abstract PolicyType Type { get; protected set; }

        public string Name { get; set; }
    }

    public abstract class OsPolicy: BasePolicy
    {
    }

    public class LinuxPolicy: OsPolicy
    {
        [JsonIgnore] public override PolicyType Type { get; protected set; } = PolicyType.Linux;
    }

    public class WindowsPolicy: OsPolicy
    {
        [JsonIgnore] public override PolicyType Type { get; protected set; } = PolicyType.Windows;
    }

    public interface IVersioned
    {
        /// <summary>The unique ID of a version of the document</summary>
        Guid VersionId { get; set; }

        /// <summary>ID that remains the same for all versions of a document</summary>
        Guid DocumentId { get; set; }
    }

    public interface IPolicy: IVersioned
    {
        PolicyType Type { get; }
        string Name { get; set; }
    }

    public enum PolicyType
    {
        Windows = 1,
        Linux = 2
    }
}
