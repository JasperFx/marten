using System;
using System.Threading.Tasks;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_687_fetching_stream_state_on_an_empty_database : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void should_return_null_state()
        {
            theSession.DocumentStore.Advanced.Clean.CompletelyRemoveAll();

            var state = theSession.Events.FetchStreamState(Guid.NewGuid());

            state.ShouldBe(null);
        }
    }
}
