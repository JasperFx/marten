using System;
using Marten.Testing.Documents;
using Marten.V4Internals;
using Shouldly;
using Xunit;

namespace Marten.Testing.V4Internals
{
    public class PersistenceGraphTests
    {
        [Fact]
        public void build_a_storage_solution()
        {
            var graph = new PersistenceGraph(new StoreOptions());

            graph.StorageFor<User>()
                .ShouldBeSameAs(graph.StorageFor<User>());
        }

        [Fact]
        public void build_a_persistence_storage_solution_for_subclass()
        {
            var options = new StoreOptions();
            options.Storage.MappingFor(typeof(User)).AddSubClass(typeof(AdminUser));

            var graph = new PersistenceGraph(options);

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
