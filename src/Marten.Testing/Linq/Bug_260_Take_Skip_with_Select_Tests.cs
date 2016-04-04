using System;
using System.Diagnostics;
using System.Linq;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class Bug_260_Take_Skip_with_Select_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void return_the_correct_number_of_results()
        {
            var targets = Target.GenerateRandomData(100);
            theStore.BulkInsert(targets.ToArray());

            var queryable = theSession.Query<Target>()
                .Skip(10)
                .Take(10)
                .Select(entity => entity.Id);


            var cmd = queryable.ToCommand(FetchType.FetchMany);


            cmd.CommandText.ShouldContain("LIMIT 10");

            queryable.ToArray().Length.ShouldBe(10);
        }
    }
}