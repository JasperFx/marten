using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class Bug_504_Take_Skip_before_Select_not_applying_sort_or_where_clause : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public async Task return_the_correct_number_of_results_when_skip_take_is_after_select_statement()
        {
            var targets = Make(100).ToArray();

            theSession.Store(targets);

            await theSession.SaveChangesAsync().ConfigureAwait(false);

            QueryStatistics stats;

            var queryable = await theSession.Query<Target>()
                .Stats(out stats)
                .Where(_ => _.Color == Colors.Blue)
                .OrderBy(_ => _.Number)
                .Select(entity => entity.Id)
                .Skip(10)
                .Take(10)
                .ToListAsync();

            stats.TotalResults.ShouldBe(33);
            queryable.Count.ShouldBe(10);
        }

        [Fact]
        public async Task return_the_correct_number_of_results_when_skip_take_is_before_select_statement()
        {
            var targets = Make(100).ToArray();

            theSession.Store(targets);

            await theSession.SaveChangesAsync().ConfigureAwait(false);

            QueryStatistics stats;

            var queryable = await theSession.Query<Target>()
                .Stats(out stats)
                .Where(_ => _.Color == Colors.Blue)
                .OrderBy(_ => _.Number)
                .Skip(10)
                .Take(10)
                .Select(entity => entity.Id)
                .ToListAsync();

            stats.TotalResults.ShouldBe(33);
            queryable.Count.ShouldBe(10);
        }

        private IEnumerable<Target> Make(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var mod2 = i % 2 == 0;
                var mod3 = i % 3 == 0;
                var color = mod3 ? Colors.Red : mod2 ? Colors.Blue : Colors.Green;

                yield return new Target
                {
                    Number = i + 1,
                    Color = color
                };
            }
        }
    }
}