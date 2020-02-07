using System.Linq;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_is_one_of_Tests : IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        [Fact]
        public void can_query_against_integers()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToArray();

            var found = theSession.Query<Target>().Where(x => x.Number.IsOneOf(validNumbers)).ToArray();

            found.Count().ShouldBeLessThan(100);

            var expected = targets
                .Where(x => validNumbers
                .Contains(x.Number))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArray();

            found.OrderBy(x => x.Id).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Fact]
        public void can_query_against_integers_with_not_operator()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToArray();

            var found = theSession.Query<Target>().Where(x => !x.Number.IsOneOf(validNumbers)).ToArray();

            var expected = targets
                .Where(x => !validNumbers
                .Contains(x.Number))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArray();

            found.OrderBy(x => x.Id).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Fact]
        public void can_query_against_integers_list()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToList();

            var found = theSession.Query<Target>().Where(x => x.Number.IsOneOf(validNumbers)).ToArray();

            found.Length.ShouldBeLessThan(100);

            var expected = targets
                .Where(x => validNumbers
                    .Contains(x.Number))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToList();

            found.OrderBy(x => x.Id).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Fact]
        public void can_query_against_integers_with_not_operator_list()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToList();

            var found = theSession.Query<Target>().Where(x => !x.Number.IsOneOf(validNumbers)).ToArray();

            var expected = targets
                .Where(x => !validNumbers
                                .Contains(x.Number))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToList();

            found.OrderBy(x => x.Id).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(expected);
        }

        public void is_one_of_example()
        {
            // SAMPLE: is_one_of
            // Finds all SuperUser's whose role is either
            // Admin, Supervisor, or Director
            var users = theSession.Query<SuperUser>()
                .Where(x => x.Role.IsOneOf("Admin", "Supervisor", "Director"));

            // ENDSAMPLE
        }

        public query_with_is_one_of_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
