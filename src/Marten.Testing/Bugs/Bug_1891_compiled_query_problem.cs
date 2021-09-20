using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Events.CodeGeneration;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
#if NET5_0
    public class Bug_1891_compiled_query_problem : BugIntegrationContext
    {
        [Fact]
        public async Task do_not_blow_up()
        {
            var results = await theSession.QueryAsync(new CompiledTimeline
            {
                Type = "foo"
            });
        }
    }

    public class CompiledTimeline : ICompiledListQuery<TimelineItem>, IQueryPlanning
    {
        public int PageSize { get; set; } = 20;

        [MartenIgnore] public int Page { private get; set; } = 1;
        public int SkipCount => (Page - 1) * PageSize;
        public string Type { get; set; }
        public Expression<Func<IMartenQueryable<TimelineItem>, IEnumerable<TimelineItem>>> QueryIs() =>
            query => query.Where(i => i.Event == Type).Skip(SkipCount).Take(PageSize);


        public void SetUniqueValuesForQueryPlanning()
        {
            Page = 3;
            PageSize = 20;
            Type = Guid.NewGuid().ToString();
        }
    }

    public record TimelineItem(Guid Id, string Event, User Raised);
#endif
}
