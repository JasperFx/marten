using Marten.Schema;
using Xunit;

namespace Marten.Testing.Schema
{
    public class SchemaBuilderTests
    {
        [Fact]
        public void can_read_text_from_resource()
        {
            SchemaBuilder.GetSqlScript(StoreOptions.DefaultDatabaseSchemaName, "mt_hilo").ShouldContain("mt_hilo");
        } 
    }
}