using Marten.Schema;
using Xunit;

namespace Marten.Testing
{
    public class document_types_must_have_an_id_member_Tests
    {
        [Fact]
        public void cannot_use_a_doc_type_with_no_id()
        {
            Exception<InvalidDocumentException>.ShouldBeThrownBy(() =>
            {
                var store = DocumentStore.For(_ =>
                {
                    _.Connection(ConnectionSource.ConnectionString);
                    _.Schema.For<BadDoc>();
                });
            });
            
        }
    }

    public class BadDoc
    {
        public string Name { get; set; }
    }
}