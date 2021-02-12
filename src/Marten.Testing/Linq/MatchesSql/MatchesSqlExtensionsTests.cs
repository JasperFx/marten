using System;
using Marten.Linq;
using Marten.Linq.MatchesSql;
using Marten.Testing.CoreFunctionality;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.MatchesSql
{
    public class MatchesSqlExtensionsTests
    {
        [Fact]
        public void Throws_NotSupportedException_when_called_directly()
        {
            ShouldThrowExtensions.ShouldThrow<NotSupportedException>(
                () => new object().MatchesSql("d.data ->> 'UserName' = ? or d.data ->> 'UserName' = ?", "baz", "jack"));
            ShouldThrowExtensions.ShouldThrow<NotSupportedException>(
                () => new object().MatchesSql(new WhereFragment("d.data ->> 'UserName' != ?", "baz")));
        }

    }
}
