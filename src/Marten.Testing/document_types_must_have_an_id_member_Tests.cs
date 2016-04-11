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
                var schema = new DocumentSchema(new StoreOptions(), null, null);
                schema.MappingFor(typeof(BadDoc)).ShouldBeNull();
            });
            
        }
    }

    public class BadDoc
    {
        public string Name { get; set; }
    }
}