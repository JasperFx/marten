using System.Linq;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using NSubstitute;
using Xunit;
using Shouldly;

namespace Marten.Testing.V4Internals
{
    [Collection("v4")]
    public class V4SessionTests : OneOffConfigurationsContext
    {
        public V4SessionTests() : base("v4")
        {
        }

        [Fact]
        public void try_to_load_from_new_session()
        {

            var target = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession.Load<Target>(target.Id).ShouldNotBeNull();

        }

        private QuerySession BuildQuerySession()
        {
            using var database =
                new ManagedConnection(new ConnectionFactory(ConnectionSource.ConnectionString), new NulloRetryPolicy());



            var newSession = new QuerySession(theStore, null, database,
                theStore.Tenancy.Default);
            return newSession;
        }

        [Fact]
        public void try_to_load_many_from_new_session()
        {

            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }


            using var newSession = BuildQuerySession();
            newSession.LoadMany<Target>(target1.Id, target2.Id, target3.Id).Count.ShouldBe(3);

        }

        [Fact]
        public void use_simple_linq_query()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            // .Where(x => x.Color == Colors.Blue)
            var targets = newSession.Query<Target>().ToList();
            targets.ShouldNotBeNull();
        }

        [Fact]
        public void use_first()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession.Query<Target>().First().ShouldNotBeNull();
            newSession.Query<Target>().FirstOrDefault().ShouldNotBeNull();
        }

        [Fact]
        public void use_single()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession.Query<Target>().Single(x => x.Id == target2.Id).ShouldNotBeNull();
            newSession.Query<Target>().SingleOrDefault(x => x.Id == target2.Id).ShouldNotBeNull();
        }

        [Fact]
        public async Task use_first_async()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                await session.SaveChangesAsync();
            }

            using var newSession = BuildQuerySession();

            (await newSession.Query<Target>().FirstAsync()).ShouldNotBeNull();
            (await newSession.Query<Target>().FirstOrDefaultAsync()).ShouldNotBeNull();
        }

        [Fact]
        public async Task use_single_async()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                await session.SaveChangesAsync();
            }

            using var newSession = BuildQuerySession();

            (await newSession.Query<Target>().SingleAsync(x => x.Id == target2.Id)).ShouldNotBeNull();
            (await newSession.Query<Target>().SingleOrDefaultAsync(x => x.Id == target2.Id)).ShouldNotBeNull();
        }


        [Fact]
        public void use_simple_linq_query_with_a_where_clause()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            var targets = newSession
                .Query<Target>()
                .Where(x => x.Color == Colors.Blue)
                .ToList();


            targets.ShouldNotBeNull();
        }


        [Fact]
        public void use_simple_linq_query_with_a_order_by_clause()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            var targets = newSession
                .Query<Target>()
                .OrderBy(x => x.Number)
                .ToList();


            targets.ShouldNotBeNull();
        }


        [Fact]
        public void use_simple_linq_query_with_skip_and_take()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            var targets = newSession
                .Query<Target>()
                .OrderBy(x => x.Number)
                .Skip(1)
                .Take(1)
                .ToList();


            targets.Count.ShouldBe(1);
        }

        [Fact]
        public void use_simple_linq_query_with_skip_and_take_to_array()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            var targets = newSession
                .Query<Target>()
                .OrderBy(x => x.Number)
                .Skip(1)
                .Take(1)
                .ToArray();


            targets.Count().ShouldBe(1);
        }


        [Fact]
        public void use_simple_linq_query_any()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession
                .Query<Target>()
                .Any(x => x.Color == target1.Color).ShouldBeTrue();


        }


        [Fact]
        public async Task use_simple_linq_query_any_async()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                await session.SaveChangesAsync();
            }

            using var newSession = BuildQuerySession();

            var matches = await newSession
                .Query<Target>()
                .AnyAsync(x => x.Color == target1.Color);

            matches.ShouldBeTrue();


        }

        [Fact]
        public void use_simple_linq_query_count()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession
                .Query<Target>()
                .Count(x => x.Color == target1.Color).ShouldBeGreaterThan(0);


        }


        [Fact]
        public async Task use_simple_linq_query_count_async()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            (await newSession
                .Query<Target>()
                .CountAsync(x => x.Color == target1.Color)).ShouldBeGreaterThan(0);


        }

        [Fact]
        public void use_simple_linq_query_count_long()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession
                .Query<Target>()
                .LongCount(x => x.Color == target1.Color).ShouldBeGreaterThan(0);


        }


        [Fact]
        public async Task use_simple_linq_query_count_async_long()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                await session.SaveChangesAsync();
            }

            using var newSession = BuildQuerySession();

            (await newSession
                .Query<Target>()
                .LongCountAsync(x => x.Color == target1.Color)).ShouldBeGreaterThan(0);


        }


        [Fact]
        public async Task use_simple_linq_query_with_skip_and_take_async()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                await session.SaveChangesAsync();
            }

            using var newSession = BuildQuerySession();

            var targets = await newSession
                .Query<Target>()
                .OrderBy(x => x.Number)
                .Skip(1)
                .Take(1)
                .ToListAsync();


            targets.Count.ShouldBe(1);
        }

        [Fact]
        public void try_to_persist_a_single_document()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();


            theStore.Tenancy.Default.EnsureStorageExists(typeof(Target));

            using var database =  new ManagedConnection(new ConnectionFactory(ConnectionSource.ConnectionString), new NulloRetryPolicy());

            using var session = new LightweightSession(theStore, new SessionOptions(), database, theStore.Tenancy.Default);

            session.Store(target1, target2, target3);
            session.SaveChanges();

            session.Load<Target>(target1.Id).ShouldNotBeNull();
        }

        [Fact]
        public void select_a_single_scalar_value()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            // .Where(x => x.Color == Colors.Blue)
            var numbers = newSession.Query<Target>().Select(x => x.Number).Take(1).ToList();
            numbers.ShouldNotBeNull();
        }

        [Fact]
        public void select_many()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession.Query<Target>().Where(x => x.Number > 0)
                .SelectMany(x => x.Children)
                .Where(x => x.Number > 1)
                .ToList()
                .ShouldNotBeNull();
        }

        [Fact]
        public void distinct_query()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession.Query<Target>().Select(x => x.Number).Distinct()
                .Count().ShouldBeGreaterThan(1);
        }

        [Fact]
        public void mathematic_functions()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            newSession.Query<Target>().Select(x => x.Double).Average().ShouldBeGreaterThan(0);
            newSession.Query<Target>().Select(x => x.Double).Min().ShouldBeGreaterThan(0);
            newSession.Query<Target>().Select(x => x.Double).Max().ShouldBeGreaterThan(0);
            newSession.Query<Target>().Select(x => x.Double).Sum().ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task mathematic_functions_async()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            (await newSession.Query<Target>().AverageAsync(x => x.Double)).ShouldBeGreaterThan(0);
            (await newSession.Query<Target>().MinAsync(x => x.Double)).ShouldBeGreaterThan(0);
            (await newSession.Query<Target>().MaxAsync(x => x.Double)).ShouldBeGreaterThan(0);
            (await newSession.Query<Target>().SumAsync(x => x.Double)).ShouldBeGreaterThan(0);
        }

        [Fact]
        public async Task distinct_query_async()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            (await newSession.Query<Target>().Select(x => x.Number).Distinct()
                .CountAsync()).ShouldBeGreaterThan(1);
        }

        [Fact]
        public void do_a_select_transform()
        {
            var target1 = Target.Random();
            var target2 = Target.Random();
            var target3 = Target.Random();

            using (var session = theStore.LightweightSession())
            {
                session.Store(target1, target2, target3);
                session.SaveChanges();
            }

            using var newSession = BuildQuerySession();

            var items = newSession.Query<Target>().Select(x => new {Number = x.Number, Date = x.Date}).ToList();

            items.ShouldNotBeNull();
        }
    }
}
