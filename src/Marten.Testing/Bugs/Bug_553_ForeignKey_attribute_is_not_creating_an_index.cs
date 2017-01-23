using System;
using System.Diagnostics;
using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_553_ForeignKey_attribute_is_not_creating_an_index : IntegratedFixture
    {

        [Fact]
        public void should_create_an_index_for_the_fk()
        {
            theStore.Schema.EnsureStorageExists(typeof(DocWithFK));

            var mapping = theStore.Schema.MappingFor(typeof(DocWithFK));
            var objects = theStore.Schema.DbObjects.FindSchemaObjects((DocumentMapping) mapping);

            objects.ActualIndices.Keys.Single()
                .ShouldBe("mt_doc_docwithfk_idx_user_id");


        }


    }

    public class DocWithFK
    {
        public Guid Id { get; set; }

        [ForeignKey(typeof(User))]
        public Guid? UserId { get; set; }
    }
}