using System;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics
{
    public class DocumentSession_disposal_locking
    {
        [Fact]
        public void throw_disposed_ex_after_disposed()
        {
            var store = DocumentStore.For(_ => _.Connection(ConnectionSource.ConnectionString));

            var session = store.OpenSession();
            session.Dispose();

            Should.Throw<ObjectDisposedException>(() =>
            {
                session.Load<User>(Guid.NewGuid());
            });


        }
    }
}
