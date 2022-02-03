using System;
using Marten.Linq.SqlProjection;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.SqlProjection
{
    public class SqlProjectionTests
    {
        [Fact]
        public void Throws_NotSupportedException_when_called_directly()
        {
            Should.Throw<NotSupportedException>(
                () => new object().SqlProjection<string>("COALESCE(d.data ->> 'UserName', ?)", "baz"));
        }
    }
}
