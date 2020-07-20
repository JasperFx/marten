using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_is_one_of_Tests : IntegrationContext
    {
        public static TheoryData<Func<int[], Expression<Func<Target, bool>>>> SupportedIsOneOfWithArray =
            new TheoryData<Func<int[], Expression<Func<Target, bool>>>>
            {
                validNumbers => x => x.Number.IsOneOf(validNumbers),
                validNumbers => x => x.Number.In(validNumbers)
            };

        public static TheoryData<Func<int[], Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithArray =
            new TheoryData<Func<int[], Expression<Func<Target, bool>>>>
            {
                validNumbers => x => !x.Number.IsOneOf(validNumbers),
                validNumbers => x => !x.Number.In(validNumbers)
            };

        public static TheoryData<Func<List<int>, Expression<Func<Target, bool>>>> SupportedIsOneOfWithList =
            new TheoryData<Func<List<int>, Expression<Func<Target, bool>>>>
            {
                validNumbers => x => x.Number.IsOneOf(validNumbers),
                validNumbers => x => x.Number.In(validNumbers)
            };

        public static TheoryData<Func<List<int>, Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithList =
            new TheoryData<Func<List<int>, Expression<Func<Target, bool>>>>
            {
                validNumbers => x => !x.Number.IsOneOf(validNumbers),
                validNumbers => x => !x.Number.In(validNumbers)
            };

        [Theory]
        [MemberData(nameof(SupportedIsOneOfWithArray))]
        public void can_query_against_integers(Func<int[], Expression<Func<Target, bool>>> isOneOf)
        {

            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToArray();

            var found = theSession.Query<Target>().Where(isOneOf(validNumbers)).ToArray();

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

        [Theory]
        [MemberData(nameof(SupportedNotIsOneOfWithArray))]
        public void can_query_against_integers_with_not_operator(Func<int[], Expression<Func<Target, bool>>> notIsOneOf)
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToArray();

            var found = theSession.Query<Target>().Where(notIsOneOf(validNumbers)).ToArray();

            var expected = targets
                .Where(x => !validNumbers
                .Contains(x.Number))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArray();

            found.OrderBy(x => x.Id).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(expected);
        }

        [Theory]
        [MemberData(nameof(SupportedIsOneOfWithList))]
        public void can_query_against_integers_list(Func<List<int>, Expression<Func<Target, bool>>> isOneOf)
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToList();

            var found = theSession.Query<Target>().Where(isOneOf(validNumbers)).ToArray();

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

        [Theory]
        [MemberData(nameof(SupportedNotIsOneOfWithList))]
        public void can_query_against_integers_with_not_operator_list(Func<List<int>, Expression<Func<Target, bool>>> notIsOneOf)
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            var validNumbers = targets.Select(x => x.Number).Distinct().Take(3).ToList();

            var found = theSession.Query<Target>().Where(notIsOneOf(validNumbers)).ToArray();

            var expected = targets
                .Where(x => !validNumbers
                                .Contains(x.Number))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToList();

            found.OrderBy(x => x.Id).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(expected);
        }

        public query_with_is_one_of_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
