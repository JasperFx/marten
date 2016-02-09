using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services.BatchedQuerying
{
    public class batched_querying_acceptance_Tests : DocumentSessionFixture<IdentityMap>
    {
        private readonly Target target1 = Target.Random();
        private readonly Target target2 = Target.Random();
        private readonly Target target3 = Target.Random();
        protected User user1 = new User { UserName = "A1", FirstName = "Justin", LastName = "Houston" };
        protected User user2 = new User { UserName = "B1", FirstName = "Tamba", LastName = "Hali" };
        protected AdminUser admin1 = new AdminUser { UserName = "A2", FirstName = "Derrick", LastName = "Johnson", Region = "Midwest" };
        protected AdminUser admin2 = new AdminUser { UserName = "B2", FirstName = "Eric", LastName = "Berry", Region = "West Coast" };
        protected SuperUser super1 = new SuperUser { UserName = "A3", FirstName = "Dontari", LastName = "Poe", Role = "Expert" };
        protected SuperUser super2 = new SuperUser { UserName = "B3", FirstName = "Sean", LastName = "Smith", Role = "Master" };

        public batched_querying_acceptance_Tests()
        {
            theStore.Schema.Alter(_ =>
            {
                _.For<User>().AddSubclass(typeof (AdminUser)).AddSubclass(typeof (SuperUser));
            });

            theSession.Store(target1, target2, target3);
            theSession.Store(user1, user2, admin1, admin2, super1, super2);

            theSession.SaveChanges();
        }

        [Fact]
        public async Task can_find_the_first_value()
        {
            var batch = theSession.CreateBatchQuery();

            var firstUser = batch.First<User>(x => x.OrderBy(_ => _.FirstName));
            var firstAdmin = batch.First<SuperUser>(x => x.OrderBy(_ => _.FirstName));

            await batch.Execute();

            (await firstUser).UserName.ShouldBe("A2");
            (await firstAdmin).UserName.ShouldBe("A3");
        }

        [Fact]
        public async Task can_find_the_first_or_default_value()
        {
            var batch = theSession.CreateBatchQuery();

            var firstUser = batch.FirstOrDefault<User>(x => x.OrderBy(_ => _.FirstName));
            var firstAdmin = batch.FirstOrDefault<SuperUser>(x => x.OrderBy(_ => _.FirstName));
            var noneUser = batch.FirstOrDefault<User>(x => x.Where(_ => _.FirstName == "not me"));

            await batch.Execute();

            (await firstUser).UserName.ShouldBe("A2");
            (await firstAdmin).UserName.ShouldBe("A3");
            (await noneUser).ShouldBeNull();
        }



        [Fact]
        public async Task can_query_documents()
        {
            var batch = theSession.CreateBatchQuery();

            var anyUsers = batch.QueryAll<User>();
            var anyAdmins = batch.QueryAll<AdminUser>();
            var anyIntDocs = batch.QueryAll<IntDoc>();
            var aUsers = batch.Query<User>(x => x.Where(_ => _.UserName.StartsWith("A")));
            var cUsers = batch.Query<User>(x => x.Where(_ => _.UserName.StartsWith("C")));

            await batch.Execute();

            (await anyUsers).OrderBy(x => x.FirstName).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, admin2.Id, user1.Id, super2.Id, user2.Id);


            (await anyAdmins).OrderBy(x => x.FirstName).Select(x => x.Id).ShouldHaveTheSameElementsAs(admin1.Id, admin2.Id);
            (await anyIntDocs).Any().ShouldBeFalse();
            (await aUsers).OrderBy(x => x.FirstName).Select(x => x.Id).ShouldHaveTheSameElementsAs(admin1.Id, super1.Id, user1.Id);
            (await cUsers).Any().ShouldBeFalse();
        }

        [Fact]
        public async Task can_query_for_any()
        {
            var batch = theSession.CreateBatchQuery();

            var anyUsers = batch.Any<User>();
            var anyAdmins = batch.Any<AdminUser>();
            var anyIntDocs = batch.Any<IntDoc>();
            var aUsers = batch.Any<User>(x => x.Where(_ => _.UserName.StartsWith("A")));
            var cUsers = batch.Any<User>(x => x.Where(_ => _.UserName.StartsWith("C")));

            await batch.Execute();

            (await anyUsers).ShouldBeTrue();
            (await anyAdmins).ShouldBeTrue();
            (await anyIntDocs).ShouldBeFalse();
            (await aUsers).ShouldBeTrue();
            (await cUsers).ShouldBeFalse();
        }

        [Fact]
        public async Task can_query_for_count()
        {
            var batch = theSession.CreateBatchQuery();

            var anyUsers = batch.Count<User>();
            var anyAdmins = batch.Count<AdminUser>();
            var anyIntDocs = batch.Count<IntDoc>();
            var aUsers = batch.Count<User>(x => x.Where(_ => _.UserName.StartsWith("A")));
            var cUsers = batch.Count<User>(x => x.Where(_ => _.UserName.StartsWith("C")));

            await batch.Execute();

            (await anyUsers).ShouldBe(6);
            (await anyAdmins).ShouldBe(2);
            (await anyIntDocs).ShouldBe(0);
            (await aUsers).ShouldBe(3);
            (await cUsers).ShouldBe(0);
        }

        [Fact]
        public async Task can_find_one_doc_at_a_time_that_is_not_in_identity_map()
        {
            var batch = theSession.CreateBatchQuery();
            var task1 = batch.Load<Target>(target1.Id);
            var task3 = batch.Load<Target>(target3.Id);

            await batch.Execute();

            (await task1).ShouldBeOfType<Target>().ShouldNotBeNull();
            (await task3).ShouldBeOfType<Target>().ShouldNotBeNull();
        }

        [Fact]
        public async Task can_find_docs_by_id_that_should_be_in_identity_map()
        {
            var batch1 = theSession.CreateBatchQuery();
            var task1 = batch1.Load<Target>(target1.Id);
            var task3 = batch1.Load<Target>(target3.Id);

            await batch1.Execute();

            var batch2 = theSession.CreateBatchQuery();
            var task21 = batch2.Load<Target>(target1.Id);
            var task23 = batch2.Load<Target>(target3.Id);

            await batch2.Execute();

            (await task1).ShouldBeSameAs(await task21);
            (await task3).ShouldBeSameAs(await task23);
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id()
        {
            var batch1 = theSession.CreateBatchQuery();
            var task = batch1.LoadMany<Target>().ById(target1.Id, target3.Id);

            await batch1.Execute();

            var list = await task;

            list.Count().ShouldBe(2);
            list.Any(x => x.Id == target1.Id).ShouldBeTrue();
            list.Any(x => x.Id == target3.Id).ShouldBeTrue();
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id_with_identity_map()
        {
            var batch1 = theSession.CreateBatchQuery();
            var task1 = batch1.LoadMany<Target>().ById(target1.Id, target3.Id);

            await batch1.Execute();

            var batch2 = theSession.CreateBatchQuery();
            var task2 = batch2.LoadMany<Target>().ById(target1.Id, target3.Id);

            await batch2.Execute();

            (await task1).ShouldHaveTheSameElementsAs(await task2);
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id_2()
        {
            var batch1 = theSession.CreateBatchQuery();
            var task = batch1.LoadMany<Target>().ByIdList(new List<Guid> { target1.Id, target3.Id });

            await batch1.Execute();

            var list = await task;

            list.Count().ShouldBe(2);
            list.Any(x => x.Id == target1.Id).ShouldBeTrue();
            list.Any(x => x.Id == target3.Id).ShouldBeTrue();
        }

        [Fact]
        public async Task can_find_multiple_docs_by_id_with_identity_map_2()
        {
            var batch1 = theSession.CreateBatchQuery();
            var task1 = batch1.LoadMany<Target>().ByIdList(new List<Guid> { target1.Id, target3.Id });

            await batch1.Execute();

            var batch2 = theSession.CreateBatchQuery();
            var task2 = batch2.LoadMany<Target>().ByIdList(new List<Guid> { target1.Id, target3.Id });

            await batch2.Execute();

            (await task1).ShouldHaveTheSameElementsAs(await task2);
        }
    }
}