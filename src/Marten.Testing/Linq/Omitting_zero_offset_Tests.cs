using System.Linq;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class Omitting_zero_offset_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Theory]
        [InlineData(0, true)]
        [InlineData(10, false)]
        public void sql_command_should_not_contain_OFFSET_with_zero_value(int skipCount, bool omit)
        {
            // given
            var queryable = theSession
                .Query<Target>()
                .Skip(skipCount);

            // when
            var sql = queryable.ToCommand().CommandText;

            // than
            if (omit)
            {
                sql.ShouldNotContain("OFFSET :", Case.Insensitive);
            }
            else
            {
                sql.ShouldContain("OFFSET :", Case.Insensitive);
            }
        }
    }
}