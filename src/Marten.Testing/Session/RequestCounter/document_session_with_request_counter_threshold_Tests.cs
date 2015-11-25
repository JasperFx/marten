using Marten.Services;
using Marten.Testing.Documents;

namespace Marten.Testing.Session.RequestCounter
{
    public class document_session_with_request_counter_threshold_Tests
    {
        public void some_test()
        {
            bool wasInvoked = false;

            var documentStore = DocumentStore.For(opt =>
            {
                opt.Connection(ConnectionSource.ConnectionString);
                opt.WithRequestThreshold(new RequestCounterThreshold(1, () => wasInvoked = true));
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
    }
}