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

namespace DocumentDbTests.Bugs
{
#if NET
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

    #region sample_implementing_iqueryplanning

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
            Page = 3; // Setting Page to 3 forces the SkipCount and PageSize to be different values
            PageSize = 20; // This has to be a positive value, or the Take() operator has no effect
            Type = Guid.NewGuid().ToString();
        }

        // And hey, if you have a public QueryStatistics member on your compiled
        // query class, you'll get the total number of records
        public QueryStatistics Statistics { get; } = new QueryStatistics();
    }

    #endregion

    public record TimelineItem(Guid Id, string Event, User Raised);
#endif
}
