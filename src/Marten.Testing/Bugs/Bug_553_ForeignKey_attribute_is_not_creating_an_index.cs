using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_553_ForeignKey_attribute_is_not_creating_an_index: IntegrationContext
    {
        [Fact]
        public async Task should_create_an_index_for_the_fk()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(DocWithFK));

            var table = await theStore.Tenancy.Default.ExistingTableFor(typeof(DocWithFK));
            table.Indexes.Any(x => x.Name == "mt_doc_docwithfk_idx_user_id").ShouldBeTrue();
        }

        public Bug_553_ForeignKey_attribute_is_not_creating_an_index(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }

    public class DocWithFK
    {
        public Guid Id { get; set; }

        [ForeignKey(typeof(User))]
        public Guid? UserId { get; set; }
    }
}
