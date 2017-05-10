using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;
using System;

namespace Marten.Testing.Linq
{
    public class query_with_escape_character : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void get_by_string_with_escape_character_shouldbe_correct()
        {
            theSession.Store(new Target { String = @"Domen\Ivan" });

            theSession.SaveChanges();

            var queryable = theSession.Query<Target>().Count(x => x.String.Equals(@"Domen\Ivan", StringComparison.OrdinalIgnoreCase));

            queryable.ShouldBe(1);
        }

        [Fact]
        public void get_by_string_with_escape_character_shouldbe_wrong()
        {
            theSession.Store(new Target { String = @"Domen\Ivan" });

            theSession.SaveChanges();

            var queryable = theSession.Query<Target>().Count(x => x.String.Equals(@"Domen\\Ivan", StringComparison.OrdinalIgnoreCase));

            queryable.ShouldBe(0);
        }
    }
}
