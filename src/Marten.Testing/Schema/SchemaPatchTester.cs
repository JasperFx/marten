using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class SchemaPatchTester
    {
        [Fact]
        public void translates_the_file_name()
        {
            SchemaPatch.ToDropFileName("update.sql").ShouldBe("update.drop.sql");
            SchemaPatch.ToDropFileName("1.update.sql").ShouldBe("1.update.drop.sql");
            SchemaPatch.ToDropFileName("folder\\1.update.sql").ShouldBe("folder\\1.update.drop.sql");
        }
    }
}