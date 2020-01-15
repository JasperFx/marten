using System;
using Marten.Services;
using Newtonsoft.Json;
using Xunit;

namespace Marten.Testing.Schema.Hierarchies
{
    public class end_to_end_document_hierarchy_with_interface_tests : DocumentSessionFixture<NulloIdentityMap>
    {
        public end_to_end_document_hierarchy_with_interface_tests()
        {
            StoreOptions(
                _ =>
                {
                    _.Schema.For<IPolicy>()
                        .Identity(x => x.VersionId)
                        .AddSubClassHierarchy();
                });
        }

        [Fact]
        public void persists_subclass()
        {
            var policy = new LinuxPolicy { Name = Guid.NewGuid().ToString() };
            using (var session = theStore.LightweightSession())
            {
                session.Store(policy);
                session.SaveChanges();
            }
        }
    }

    public abstract class BasePolicy : IPolicy
    {
        public Guid VersionId { get; set; } = Guid.NewGuid();
        public Guid DocumentId { get; set; } = Guid.NewGuid();

        [JsonIgnore]
        public abstract PolicyType Type { get; protected set; }

        public string Name { get; set; }
    }

    public abstract class OsPolicy : BasePolicy
    {
    }

    public class LinuxPolicy : OsPolicy
    {
        [JsonIgnore]
        public override PolicyType Type { get; protected set; } = PolicyType.Linux;
    }

    public class WindowsPolicy : OsPolicy
    {
        [JsonIgnore]
        public override PolicyType Type { get; protected set; } = PolicyType.Windows;
    }

    public interface IVersioned
    {
        /// <summary>The unique ID of a version of the document</summary>
        Guid VersionId { get; set; }

        /// <summary>ID that remains the same for all versions of a document</summary>
        Guid DocumentId { get; set; }
    }

    public interface IPolicy : IVersioned
    {
        PolicyType Type { get; }
        string Name { get; set; }
    }

    public enum PolicyType
    {
        Windows = 1,
        Linux = 2,
    }
}