using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if NET46
using System.Transactions;
#endif
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
#if NET46
    public class Bug_949_connection_closing_issue_with_transaction_scope : IntegratedFixture
    {
        [Fact]
        public void do_not_blow_up_with_too_many_open_connections()
        {

            for (int i = 0; i < 1000; i++)
            {
                // this reaches 200, than crashes

                using (var scope = new TransactionScope())
                {
                    using (var session = theStore.OpenSession(SessionOptions.ForCurrentTransaction()))
                    {
                        session.Store(new EntityToSave());
                        session.SaveChanges();
                    }

                    scope.Complete();
                }
            }

            using (var session = theStore.QuerySession())
            {
                session.Query<EntityToSave>().Count().ShouldBe(1000);
            }
            
        }
    }

    class EntityToSave
    {
        public Guid Id { get; set; }
    }
#endif
}
