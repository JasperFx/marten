using System;
using Marten.Events;
using Xunit;

namespace Marten.Testing.Events
{
    public class start_stream_should_enforce_that_it_is_a_new_stream : IntegratedFixture
    {
        [Fact]
        public void throw_exception_if_start_stream_is_called_on_existing_stream()
        {
            var stream = Guid.NewGuid();

            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream(stream, new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Events.StartStream(stream, new MembersJoined());
                Exception<ExistingStreamIdCollisionException>.ShouldBeThrownBy(() =>
                {
                    session.SaveChanges();
                });
            }
        }
    }
}