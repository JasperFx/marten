using System;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.V4Internals
{
    public class PersistenceGraphTests
    {
        [Fact]
        public void build_a_storage_solution()
        {
            var graph = new ProviderGraph(new StoreOptions());

            graph.StorageFor<User>()
                .ShouldBeSameAs(graph.StorageFor<User>());
        }

        [Fact]
        public void build_a_persistence_storage_solution_for_subclass()
        {
            var options = new StoreOptions();
            options.Storage.MappingFor(typeof(User)).AddSubClass(typeof(AdminUser));

            var graph = new ProviderGraph(options);

            var persistence = graph.StorageFor<AdminUser>();
            persistence
                .ShouldBeSameAs(graph.StorageFor<AdminUser>());

            persistence.Lightweight.ShouldBeOfType<SubClassDocumentStorage<AdminUser, User, Guid>>();
            persistence.QueryOnly.ShouldBeOfType<SubClassDocumentStorage<AdminUser, User, Guid>>();
            persistence.IdentityMap.ShouldBeOfType<SubClassDocumentStorage<AdminUser, User, Guid>>();
            persistence.DirtyTracking.ShouldBeOfType<SubClassDocumentStorage<AdminUser, User, Guid>>();
            persistence.BulkLoader.ShouldBeOfType<SubClassBulkLoader<AdminUser, User>>();
        }

    }
}
