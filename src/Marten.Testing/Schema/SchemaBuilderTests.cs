using Marten.Schema;
using Xunit;

namespace Marten.Testing.Schema
{
    public class SchemaBuilderTests
    {
        [Fact]
        public void can_read_text_from_resource()
        {
            SchemaBuilder.GetText("mt_hilo").ShouldContain("mt_hilo");
        } 
    }
}