using Marten.Schema;

namespace Marten.Testing.Schema
{
    public class SchemaBuilderTests
    {
        public void can_read_text_from_resource()
        {
            SchemaBuilder.GetText("mt_hilo").ShouldContain("mt_hilo");
        } 
    }
}