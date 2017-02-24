using System;
using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [SingleStoryteller]
    public class invoking_queryable_through_first_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void first_hit_with_only_one_document()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().First(x => x.Number == 3)
                .ShouldNotBeNull();
        }

        [Fact]
        public void first_or_default_hit_with_only_one_document()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().FirstOrDefault(x => x.Number == 3)
                .ShouldNotBeNull();
        }

        [Fact]
        public void first_or_default_miss()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().FirstOrDefault(x => x.Number == 11)
                .ShouldBeNull();
        }

        [Fact]
        public void first_correct_hit_with_more_than_one_match()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2, Flag = true });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Number == 2).First().Flag
                .ShouldBeTrue();
        }

        [Fact]
        public void first_or_default_correct_hit_with_more_than_one_match()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2, Flag = true });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Number == 2).FirstOrDefault().Flag
                .ShouldBeTrue();
        }

        [Fact]
        public void first_miss()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.SaveChanges();

            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                theSession.Query<Target>().Where(x => x.Number == 11).First();
            });
        }
    }
}