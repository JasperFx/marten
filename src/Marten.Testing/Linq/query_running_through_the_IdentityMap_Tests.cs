using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Testing.Examples;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_running_through_the_IdentityMap_Tests : DocumentSessionFixture<IdentityMap>
    {
        private User user1;
        private User user2;
        private User user3;
        private User user4;

        public query_running_through_the_IdentityMap_Tests()
        {
            // SAMPLE: using-store-with-multiple-docs
            user1 = new User {FirstName = "Jeremy"};
            user2 = new User {FirstName = "Jens"};
            user3 = new User {FirstName = "Jeff"};
            user4 = new User {FirstName = "Corey"};

            theSession.Store(user1, user2, user3, user4);
            // ENDSAMPLE

            theSession.SaveChanges();

            theSession.Load<User>(user1.Id).ShouldBeTheSameAs(user1);
        }

        [Fact]
        public void single_runs_through_the_identity_map()
        {
            theSession.Query<User>().Where(x => x.FirstName == "Jeremy")
                .Single().ShouldBeTheSameAs(user1);

            theSession.Query<User>().Where(x => x.FirstName == user4.FirstName)
                .SingleOrDefault().ShouldBeTheSameAs(user4);


        }


        [Fact]
        public void first_runs_through_the_identity_map()
        {
            theSession.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
                .First().ShouldBeTheSameAs(user3);


            theSession.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
                .FirstOrDefault().ShouldBeTheSameAs(user3);

        }

        [Fact]
        public void query_runs_through_identity_map()
        {
            var users = theSession.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
                .ToArray();

            users[0].ShouldBeTheSameAs(user3);
            users[1].ShouldBeTheSameAs(user2);
            users[2].ShouldBeTheSameAs(user1);

        }

        [Fact]
        public async Task single_runs_through_the_identity_map_async()
        {
            var u1 = await theSession.Query<User>().Where(x => x.FirstName == "Jeremy")
                .SingleAsync().ConfigureAwait(false);

            u1.ShouldBeTheSameAs(user1);

            var u2 = await theSession.Query<User>().Where(x => x.FirstName == user4.FirstName)
                .SingleOrDefaultAsync().ConfigureAwait(false);
            
            u2.ShouldBeTheSameAs(user4);


        }



        [Fact]
        public async Task first_runs_through_the_identity_map_async()
        {
            var u1 = await theSession.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
                .FirstAsync().ConfigureAwait(false);
            
            u1.ShouldBeTheSameAs(user3);


            var u2 = await theSession.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
                .FirstOrDefaultAsync().ConfigureAwait(false);
            
            u2.ShouldBeTheSameAs(user3);

        }


        [Fact]
        public async Task query_runs_through_identity_map_async()
        {
            var users = await theSession.Query<User>().Where(x => x.FirstName.StartsWith("J")).OrderBy(x => x.FirstName)
                .ToListAsync().ConfigureAwait(false);

            users[0].ShouldBeTheSameAs(user3);
            users[1].ShouldBeTheSameAs(user2);
            users[2].ShouldBeTheSameAs(user1);

        }
    }
}