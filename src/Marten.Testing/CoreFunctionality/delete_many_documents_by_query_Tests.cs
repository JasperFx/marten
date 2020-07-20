using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.CoreFunctionality
{
    public class delete_many_documents_by_query_Tests : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        [Fact]
        public void can_delete_by_query()
        {
            var targets = Target.GenerateRandomData(50).ToArray();
            for (var i = 0; i < 15; i++)
            {
                targets[i].Double = 578;
            }

            theStore.BulkInsert(targets);

            var initialCount = theSession.Query<Target>().Where(x => x.Double == 578).Count();

            // SAMPLE: DeleteWhere
            theSession.DeleteWhere<Target>(x => x.Double == 578);

            theSession.SaveChanges();
            // ENDSAMPLE

            theSession.Query<Target>().Count().ShouldBe(50 - initialCount);

            theSession.Query<Target>().Where(x => x.Double == 578).Count()
                .ShouldBe(0);

        }

        [Fact]
        public void can_delete_by_query_with_complex_where_clauses()
        {
            var targets = Target.GenerateRandomData(50).ToArray();
            for (var i = 0; i < 15; i++)
            {
                targets[i].Double = 578;
            }

            theStore.BulkInsert(targets);

            var current = new IntDoc {Id = 5};

            theSession.DeleteWhere<Target>(x => x.Double == 578 && x.Number == current.Id);

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Double == 578 && x.Number == current.Id).Count()
                .ShouldBe(0);

        }



        [Fact]
        public void in_a_mix_with_other_commands()
        {
            var targets = Target.GenerateRandomData(50).ToArray();
            for (var i = 0; i < 15; i++)
            {
                targets[i].Double = 578;
            }

            theStore.BulkInsert(targets);

            var initialCount = theSession.Query<Target>().Where(x => x.Double == 578).Count();

            theSession.Store(new User(), new User(), new User());
            theSession.DeleteWhere<Target>(x => x.Double == 578);
            theSession.SaveChanges();

            theSession.Query<Target>().Count().ShouldBe(50 - initialCount);

            theSession.Query<Target>().Where(x => x.Double == 578).Count()
                .ShouldBe(0);

            theSession.Query<User>().Count().ShouldBe(3);
        }

        public class FailureInLife
        {
            public int Id { get; set; }
            public int What { get; set; }
        }

        [Fact]
        public void can_delete_by_query_multiple()
        {
            var targets = new[] { new FailureInLife { Id = 1, What = 2 } };

            theStore.BulkInsert(targets);
            var id = 1;
            var what = 2;

            theSession.DeleteWhere<FailureInLife>(x => x.Id == id && x.What == what);

            theSession.SaveChanges();
            // ENDSAMPLE

            theSession.Query<FailureInLife>().Count().ShouldBe(0);

        }

        public delete_many_documents_by_query_Tests(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }
    }
}
