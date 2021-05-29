using System;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Linq.SoftDeletes;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using StoryTeller;
using StoryTeller.Grammars.Tables;

namespace Marten.Storyteller.Fixtures
{
    public class DocumentFilteringFixture: Fixture
    {
        public DocumentFilteringFixture()
        {
            Title = "Default Document Filtering";
        }


        private static DocumentStore buildStore(bool softDeleted, TenancyStyle tenancy)
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
                    _.Connection(ConnectionSource.ConnectionString);
                    _.Policies.AllDocumentsAreMultiTenanted();
                }
                else
                {
                    _.Connection(ConnectionSource.ConnectionString);
                }
            });
            return store;
        }

        [ExposeAsTable("Additive Filtering on User and AdminUser documents")]
        public void AdditiveFilters(
            [Header("User Supplied Where"), SelectionValues("x => x.UserName == \"Aubrey\"", "x => x.MaybeDeleted()")]string baseWhere,
            [Header("Soft Deleted")] bool softDeleted,
            [Header("Tenancy")] TenancyStyle tenancy,
            [Header("User")]out string user, [Header("AdminUser")]out string subclass

            )
        {
            var store = buildStore(softDeleted, tenancy);

            using (var session = store.QuerySession())
            {
                IQueryable<User> userQuery = null;
                IQueryable<AdminUser> adminQuery = null;

                if (baseWhere == "x => x.UserName == \"Aubrey\"")
                {
                    userQuery = session.Query<User>().Where(x => x.UserName == "Aubrey");
                    adminQuery = session.Query<AdminUser>().Where(x => x.UserName == "Aubrey");
                }
                else
                {
                    userQuery = session.Query<User>().Where(x => x.MaybeDeleted());
                    adminQuery = session.Query<AdminUser>().Where(x => x.MaybeDeleted());
                }

                var userCommand = userQuery.ToCommand();
                var adminCommand = adminQuery.ToCommand();

                user = extractWhereClause(userCommand);
                subclass = extractWhereClause(adminCommand);
            }
        }

        private string extractWhereClause(NpgsqlCommand cmd)
        {
            var index = cmd.CommandText.ToLower().IndexOf("where");
            return cmd.CommandText.Substring(index + 5).Trim();
        }
    }
}
