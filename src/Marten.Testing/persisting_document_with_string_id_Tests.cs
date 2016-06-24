using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class Account
    {
        public string Id { get;set; }
    }

    public class persisting_document_with_string_id_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void persist_and_load()
        {
            var account = new Account{Id = "email@server.com"};

            theSession.Store(account);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                session.Load<Account>("email@server.com")
                    .ShouldNotBeNull();

                session.Load<Account>("nonexistent@server.com")
                    .ShouldBeNull();
            }

        }

        // SAMPLE: persist_and_load_async
        [Fact]
        public async Task persist_and_load_async()
        {
            var account = new Account { Id = "email@server.com" };

            theSession.Store(account);
            await theSession.SaveChangesAsync().ConfigureAwait(false);

            using (var session = theStore.OpenSession())
            {
                (await session.LoadAsync<Account>("email@server.com").ConfigureAwait(false)).ShouldNotBeNull();

                (await session.LoadAsync<Account>("nonexistent@server.com").ConfigureAwait(false)).ShouldBeNull();
            }
        }
        // ENDSAMPLE

        [Fact]
        public void throws_exception_if_trying_to_save_null_id()
        {
            var account = new Account {Id = null};

            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                theSession.Store(account);
            });

            
        }


        [Fact]
        public void throws_exception_if_trying_to_save_empty_id()
        {
            var account = new Account { Id = string.Empty };

            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                theSession.Store(account);
            });


        }

        [Fact]
        public void persist_and_delete()
        {
            var account = new Account { Id = "email@server.com" };

            theSession.Store(account);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                session.Delete<Account>(account.Id);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Load<Account>(account.Id)
                    .ShouldBeNull();
            }
        }

        [Fact]
        public void load_by_array_of_ids()
        {
            theSession.Store(new Account { Id = "A" });
            theSession.Store(new Account { Id = "B" });
            theSession.Store(new Account { Id = "C" });
            theSession.Store(new Account { Id = "D" });
            theSession.Store(new Account { Id = "E" });

            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                session.LoadMany<Account>("A", "B", "E").Count().ShouldBe(3);
            }
        }
    }
}