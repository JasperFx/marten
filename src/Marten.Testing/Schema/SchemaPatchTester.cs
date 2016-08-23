using System.IO;
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

        [Fact]
        public void write_transactional_script_with_no_role()
        {
            var rules = new DdlRules();
            rules.Role.ShouldBeNull();

            var patch = new SchemaPatch(rules);

            var writer = new StringWriter();

            patch.WriteTransactionalScript(writer, w =>
            {
                w.WriteLine("Hello.");
            });

            writer.ToString().ShouldNotContain("SET ROLE");
            writer.ToString().ShouldNotContain("RESET ROLE;");
        }

        [Fact]
        public void write_transactional_script_with_a_role()
        {
            var rules = new DdlRules();
            rules.Role = "OCD_DBA";

            var patch = new SchemaPatch(rules);

            var writer = new StringWriter();

            patch.WriteTransactionalScript(writer, w =>
            {
                w.WriteLine("Hello.");
            });

            writer.ToString().ShouldContain("SET ROLE OCD_DBA;");
            writer.ToString().ShouldContain("RESET ROLE;");
        }
    }
}