using Marten.Generation.Templates;
using Shouldly;

namespace Marten.Testing.Generation
{
    public class TemplateReadingTests
    {
        public void can_read_document_table()
        {
            Templates.DocumentTable().ShouldContain("%TABLE_NAME%");
        } 
    }
}