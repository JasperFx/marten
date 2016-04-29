using Marten.Schema;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class TableNameTests
    {
        [Fact]
        public void owner_name_has_no_schema_if_schema_is_public()
        {
            var table = new TableName("public", "mt_doc_user");
            table.OwnerName.ShouldBe("mt_doc_user");
        }

        [Fact]
        public void owner_name_has_schema_if_not_in_public()
        {
            var table = new TableName("other", "mt_doc_user");
            table.OwnerName.ShouldBe("other.mt_doc_user");
        }
    }
}