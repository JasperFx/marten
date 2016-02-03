using Marten.Schema;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing
{
    public class DocumentMappingTests
    {
        [Fact]
        public void you_cannot_assign_a_table_name_that_is_not_prefaced_with_mt_doc()
        {
            var mapping = DocumentMapping.For<User>();
        } 
    }
}