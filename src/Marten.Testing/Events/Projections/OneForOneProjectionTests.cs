using System.Linq;
using CodeTracker;
using Marten.Events.Projections;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Projections
{
    public class OneForOneProjectionTests
    {
        [Fact]
        public void consumes_the_event()
        {
            new OneForOneProjection<Commit,CommitView>(new CommitViewTransform())
                .Consumes.Single()
                .ShouldBe(typeof(Commit));
        }

        [Fact]
        public void produces_the_view()
        {
            new OneForOneProjection<Commit, CommitView>(new CommitViewTransform())
                .Produces.Single()
                .ShouldBe(typeof(CommitView));
        }
    }
}