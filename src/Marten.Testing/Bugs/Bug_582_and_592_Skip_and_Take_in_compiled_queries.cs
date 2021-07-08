using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_582_and_592_Skip_and_Take_in_compiled_queries: BugIntegrationContext
    {
        [Fact]
        public void can_get_separate_pages()
        {
            var targets = Target.GenerateRandomData(1000).ToArray();

            theStore.BulkInsert(targets);

            using (var query = theStore.QuerySession())
            {
                var page1 = query.Query(new PageOfTargets { Start = 10, Take = 17 }).ToList();
                var page2 = query.Query(new PageOfTargets { Start = 50, Take = 11 }).ToList();

                page1.Count().ShouldBe(17);
                page2.Count().ShouldBe(11);

                foreach (var target in page1)
                {
                    page2.Any(x => x.Id == target.Id).ShouldBeFalse();
                }
            }
        }

        [Fact]
        public void can_get_separate_pages_with_enum_strings()
        {
            StoreOptions(_ =>
            {
                _.UseDefaultSerialization(EnumStorage.AsString);
            });

            var targets = Target.GenerateRandomData(1000).ToArray();

            theStore.BulkInsert(targets);

            using (var query = theStore.QuerySession())
            {
                var page1 = query.Query(new PageOfTargets { Start = 10, Take = 17 }).ToList();
                var page2 = query.Query(new PageOfTargets { Start = 50, Take = 11 }).ToList();

                page1.Count().ShouldBe(17);
                page2.Count().ShouldBe(11);

                foreach (var target in page1)
                {
                    page2.Any(x => x.Id == target.Id).ShouldBeFalse();
                }
            }
        }

    }

    public class PageOfTargets: ICompiledListQuery<Target>
    {
        public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
        {
            return q => q.Where(x => x.Color == Color).OrderBy(x => x.Id).Skip(Start).Take(Take);
        }

        public Colors Color { get; set; } = Colors.Blue;
        public int Start { get; set; } = 0;
        public int Take { get; set; } = 10;
    }

    public class WrongOrderedPageOfTargets: ICompiledListQuery<Target>
    {
        public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
        {
            return q => q.Where(x => x.Color == Color).OrderBy(x => x.Id).Take(Take).Skip(Start);
        }

        public Colors Color { get; set; } = Colors.Blue;
        public int Start { get; set; } = 0;
        public int Take { get; set; } = 10;
    }
}
