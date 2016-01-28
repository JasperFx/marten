using System;
using System.Diagnostics;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Session.RequestCounter
{
    public class document_session_with_request_counter_threshold_Tests
    {
        [Fact]
        public void should_invoke_defined_action_if_threshold_is_exceeded()
        {
            bool wasInvoked = false;

            var documentStore = DocumentStore.For(opt =>
            {
                opt.AutoCreateSchemaObjects = true;
                opt.Connection(ConnectionSource.ConnectionString);
                opt.RequestCounterThreshold = new RequestCounterThreshold(1, () => wasInvoked = true);
            });

            using (var session = documentStore.OpenSession())
            {
                session.Store(new User {FirstName = "Jens"});
                session.SaveChanges();

                session.Store(new User { FirstName = "Ida"});
                session.SaveChanges(); //this is the second request, should invoke threshold
            }

            wasInvoked.ShouldBeTrue();
        }

        private bool isOurAppInDevelopmentMode()
        {
            return true;
        }

        public void samples()
        {
            // SAMPLE: request-counter-trips-off-debug-message
            var store1 = DocumentStore.For(opt =>
            {
                opt.Connection("some connection string");
                opt.RequestCounterThreshold = new RequestCounterThreshold(1, () =>
                {
                    Debug.WriteLine("Too many database requests!");
                });
            });
            // ENDSAMPLE

            // SAMPLE: request-counter-throws-exception
            var store2 = DocumentStore.For(opt =>
            {
                opt.Connection("some connection string");

                // Use some kind of conditional logic to know when it's valid
                // to opt into the request counter threshold
                if (isOurAppInDevelopmentMode())
                {
                    opt.RequestCounterThreshold = new RequestCounterThreshold(1, () =>
                    {
                        throw new InvalidOperationException("Too many database requests!");
                    });
                }
            });
            // ENDSAMPLE
        }
    }
}