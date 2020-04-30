using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1296_sessionlistener_updates : IntegrationContext
    {
        [Fact]
        public void bug()
        {
            var user = new User();

            using (var session = theStore.OpenSession())
            {
                session.Insert(user);
                session.SaveChanges();
            }

            var updates = 0;

            using (var session = theStore.OpenSession(new SessionOptions()
            {
                Listeners = { new CustomDocumentSessionListener(work =>
                {
                    updates = work.UpdatesFor<User>().Count();
                }) }
            }))
            {
                var u = session.Load<User>(user.Id);
                u.FirstName = "updated";
                session.Update(u);
                session.SaveChanges();
            }

            Assert.Equal(1, updates);
        }

        public class CustomDocumentSessionListener: DocumentSessionListenerBase
        {
            private readonly Action<IUnitOfWork> onUpdate;

            public CustomDocumentSessionListener(Action<IUnitOfWork> onUpdate)
            {
                this.onUpdate = onUpdate;
            }

            public override void BeforeSaveChanges(IDocumentSession session)
            {
                onUpdate(session.PendingChanges);
            }
        }

        public Bug_1296_sessionlistener_updates(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
