using Marten.Generation;
using Shouldly;

namespace Marten.Testing.Generation
{
    public class SchemaBuilderTests
    {
        public void table_name_for_document()
        {
            SchemaBuilder.TableNameFor(typeof(MySpecialDocument))
                .ShouldBe("mt_doc_MySpecialDocument");
        }

        public class MySpecialDocument { }
    }
}