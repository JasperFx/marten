using System;
using Marten.Linq.Filters;
using Marten.Linq.MatchesSql;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace Marten.Testing.Linq.MatchesSql
{
    public class MatchesSqlExtensionsTests
    {
        [Fact]
        public void Throws_NotSupportedException_when_called_directly()
        {
            Should.Throw<NotSupportedException>(
                () => new object().MatchesSql("d.data ->> 'UserName' = ? or d.data ->> 'UserName' = ?", "baz", "jack"));
            Should.Throw<NotSupportedException>(
                () => new object().MatchesSql(new WhereFragment("d.data ->> 'UserName' != ?", "baz")));
        }
    }
}
