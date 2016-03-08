using System;
using Marten.Events;
using Xunit;

namespace Marten.Testing.Events
{
    public class AggregateModelTests
    {
        [Fact]
        public void cannot_accept_a_type_that_is_not_an_aggregate()
        {
            Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
            {
                new AggregateModel(GetType());
            });

            Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
            {
                new AggregateModel(GetType(), "some alias");
            });
        } 
    }
}