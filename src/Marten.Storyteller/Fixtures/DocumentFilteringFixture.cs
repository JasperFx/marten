using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Documents;
using StoryTeller;
using StoryTeller.Grammars.Tables;

namespace Marten.Storyteller.Fixtures
{
    public class DocumentFilteringFixture : Fixture
    {
        public DocumentFilteringFixture()
        {
            Title = "Default Document Filtering";
        }

        [ExposeAsTable("Default Filter Determination for User and AdminUser subclass")]
        public void FiltersAre(
            [Header("Soft Deleted")] bool softDeleted,
            [Header("Tenancy")] TenancyStyle tenancy, 
            [Header("User")]out string user, [Header("AdminUser")]out string subclass)
        {
            var store = DocumentStore.For(_ =>
            {
                _.Schema.For<User>().AddSubClass<AdminUser>();

                if (softDeleted)
                {
                    _.Schema.For<User>().SoftDeleted();
                }

                if (tenancy == TenancyStyle.Conjoined)
                {
                    _.Connection(ConnectionSource.ConnectionString)
                        .MultiTenanted();
                }
                else
                {
                    _.Connection(ConnectionSource.ConnectionString);
                }
            });


            user = store.Storage.MappingFor(typeof(User)).DefaultWhereFragment().ToSql();

            subclass = store
                .Tenancy.Default.MappingFor(typeof(AdminUser)).As<IQueryableDocument>()
                .DefaultWhereFragment().ToSql();

        }
    }


}