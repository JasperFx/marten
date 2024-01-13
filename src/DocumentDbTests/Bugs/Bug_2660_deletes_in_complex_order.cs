using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace DocumentDbTests.Bugs;

public class Bug_2660_deletes_in_complex_order : BugIntegrationContext
{
        // works
        [Fact]
        public async Task Deleting_Single_DocType_In_One_Session_Works()
        {
            // store & delete within same session
            await using var session = TheStore.IdentitySession();
            var id = Guid.NewGuid();
            session.Store(new FooModel() { Id = id });
            session.Delete<FooModel>(id);
            await session.SaveChangesAsync();

            await using var session2 = TheStore.IdentitySession();
            var model = await session.LoadAsync<FooModel>(id);
            model.ShouldBeNull();
        }

        // fails
        [Fact]
        public async Task Deleting_Multiple_DocTypes_In_One_Session_Works()
        {
            // store & delete within same session
            await using var session = TheStore.IdentitySession();
            var id = Guid.NewGuid();
            session.Store(new FooModel() { Id = id });
            session.Store(new BarModel() { Id = id });
            session.Delete<FooModel>(id);
            session.Delete<BarModel>(id);

            session.PendingChanges.Operations().Count().ShouldBe(2);
            session.PendingChanges.Deletions().Count().ShouldBe(2);

            await session.SaveChangesAsync();

            await using var session2 = TheStore.IdentitySession();
            var model = await session.LoadAsync<FooModel>(id);
            model.ShouldBeNull();
        }

        // also fails
        [Fact]
        public async Task Delete_Where_Within_Same_Session_Doesnt_Affect_Actual_Delete()
        {
            // store & delete in the same session
            using var session = TheStore.IdentitySession();
            var id = Guid.NewGuid();
            session.Store(new FooModel() { Id = id });
            session.Store(new BarModel() { Id = Guid.NewGuid(), RelGuid = id });
            session.Delete<FooModel>(id);
            session.DeleteWhere<BarModel>(x => x.RelGuid == id);
            await session.SaveChangesAsync();

            await using var session2 = TheStore.IdentitySession();
            var model = await session.LoadAsync<FooModel>(id);
            model.ShouldBeNull();
        }

        [Fact]
        public async Task mixing_id_types_lightweight()
        {
            await TheStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(IntDoc));
            await TheStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(GuidDoc));

            await using var session1 = TheStore.LightweightSession();

            const int id = 5;

            // store & delete guid doc
            var guid = Guid.NewGuid();

            session1.Store(new GuidDoc{Id = guid});
            session1.Delete<GuidDoc>(guid);

            // store & delete int doc
            session1.Store(new IntDoc(id));
            session1.Delete<IntDoc>(id);

            await session1.SaveChangesAsync();

            await using var session2 = TheStore.QuerySession();
            (await session2.Query<IntDoc>().CountAsync()).ShouldBe(0);
            (await session2.Query<GuidDoc>().CountAsync()).ShouldBe(0);
        }

        [Fact]
        public async Task mixing_id_types_identity()
        {
            await TheStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(IntDoc));
            await TheStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(GuidDoc));

            await using var session1 = TheStore.IdentitySession();

            const int id = 5;

            // store & delete guid doc
            var guid = Guid.NewGuid();

            session1.Store(new GuidDoc{Id = guid});
            session1.Delete<GuidDoc>(guid);

            // store & delete int doc
            session1.Store(new IntDoc(id));
            session1.Delete<IntDoc>(id);

            await session1.SaveChangesAsync();

            await using var session2 = TheStore.QuerySession();
            (await session2.Query<IntDoc>().CountAsync()).ShouldBe(0);
            (await session2.Query<GuidDoc>().CountAsync()).ShouldBe(0);
        }

        [Fact]
        public async Task mixing_id_types_dirty_checking()
        {
            await TheStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(IntDoc));
            await TheStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(GuidDoc));

            await using var session1 = TheStore.DirtyTrackedSession();

            const int id = 5;

            // store & delete guid doc
            var guid = Guid.NewGuid();

            session1.Store(new GuidDoc{Id = guid});
            session1.Delete<GuidDoc>(guid);

            // store & delete int doc
            session1.Store(new IntDoc(id));
            session1.Delete<IntDoc>(id);

            await session1.SaveChangesAsync();

            await using var session2 = TheStore.QuerySession();
            (await session2.Query<IntDoc>().CountAsync()).ShouldBe(0);
            (await session2.Query<GuidDoc>().CountAsync()).ShouldBe(0);
        }

}

public class FooModel
{
    public Guid Id { get; set; }
}

public class BarModel
{
    public Guid Id { get; set; }
    public Guid RelGuid { get; set; }
}
