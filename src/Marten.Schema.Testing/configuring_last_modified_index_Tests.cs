using System.Linq;
using Shouldly;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Schema.Testing
{
    public class configuring_last_modified_index_Tests
    {
        [Fact]
        public void creates_btree_index_for_mt_last_modified()
        {
            var mapping = DocumentMapping.For<Customer>();
            var indexDefinition = mapping.Indexes.Cast<DocumentIndex>().Single(x => x.Columns.First() == SchemaConstants.LastModifiedColumn);

            indexDefinition.Method.ShouldBe(IndexMethod.btree);
        }

        #region sample_index-last-modified-via-attribute
        [IndexedLastModified]
        public class Customer
        {
        }
        #endregion sample_index-last-modified-via-attribute
    }
}
