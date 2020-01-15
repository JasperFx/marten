using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Linq.SoftDeletes;
using Marten.Testing;
using Marten.Testing.Documents;
using StoryTeller;
using StoryTeller.Grammars.Tables;

namespace Marten.Storyteller.Fixtures.MultiTenancy
{
    public class ConfigureDocumentStoreFixture: Fixture
    {
        private StoreOptions _options;

        public override void SetUp()
        {
            _options = new StoreOptions();
            _options.Connection(ConnectionSource.ConnectionString);
        }

        [FormatAs("Uses Conjoined Multi-Tenancy")]
        public void UsesConjoinedMultiTenancy()
        {
            _options.Connection(ConnectionSource.ConnectionString);
            _options.Policies.AllDocumentsAreMultiTenanted();
        }

        [FormatAs("User documents are soft deleted")]
        public void UsersAreSoftDeleted()
        {
            _options.Schema.For<User>().SoftDeleted();
        }

        [FormatAs("User documents also include AdminUser and SuperUser subclasses")]
        public void UserIsHierarchical()
        {
            _options.Schema.For<User>().AddSubClass<AdminUser>().AddSubClass<SuperUser>();
        }

        public override void TearDown()
        {
            var store = new DocumentStore(_options);
            store.Advanced.Clean.CompletelyRemoveAll();
            Context.State.Store(store);
        }
    }

    public class MultiTenancyQueryingFixture: Fixture
    {
        private DocumentStore _store;

        public MultiTenancyQueryingFixture()
        {
            Title = "Multi Tenancy Scenarios";
            AddSelectionValues("UserTypes", "User", "AdminUser", "SuperUser");
        }

        public IGrammar IfTheStoreIs()
        {
            return Embed<ConfigureDocumentStoreFixture>("If the DocumentStore is configured as")
                .After(c => _store = c.State.Retrieve<DocumentStore>());
        }

        public override void SetUp()
        {
            _users.Clear();
        }

        public override void TearDown()
        {
            _store.Dispose();
        }

        private readonly IDictionary<string, User> _users = new Dictionary<string, User>();

        [ExposeAsTable("If the users are")]
        public void TheUsersAre(
            [Header("Tenant Id")] string tenant,
            string UserName,
            [SelectionList("UserTypes")] string UserType)
        {
            User user = new User();
            switch (UserType)
            {
                case "User":
                    user = new User { UserName = UserName };
                    break;

                case "AdminUser":
                    user = new AdminUser { UserName = UserName };
                    break;

                case "SuperUser":
                    user = new SuperUser { UserName = UserName };
                    break;
            }

            _users.Add(user.UserName, user);

            using (var session = _store.OpenSession(tenant))
            {
                session.Store(user);
                session.SaveChanges();
            }
        }

        [FormatAs("Try to delete User {name} by id as tenant {tenantId}")]
        public void Delete(string name, string tenantId)
        {
            using (var session = _store.OpenSession(tenantId))
            {
                var user = _users[name];
                session.Delete(user);
                session.SaveChanges();
            }
        }

        [FormatAs("Try to delete all users w/ a user name starting with 'A' from tenant {tenantId}")]
        public void DeleteByFilter(string tenantId)
        {
            using (var session = _store.OpenSession(tenantId))
            {
                session.DeleteWhere<User>(x => x.UserName.StartsWith("A"));
                session.SaveChanges();
            }
        }

        [FormatAs("Try to delete admin users w/ a user name starting with 'A' from tenant {tenantId}")]
        public void DeleteAdminByFilter(string tenantId)
        {
            using (var session = _store.OpenSession(tenantId))
            {
                session.DeleteWhere<AdminUser>(x => x.UserName.StartsWith("A"));
                session.SaveChanges();
            }
        }

        [ExposeAsTable("Can load a user document by id and tenant")]
        [return: Header("Returns a Document")]
        public bool CanLoadById([Header("Tenant Id")] string tenant, string UserName)
        {
            var id = _users[UserName].Id;

            using (var session = _store.QuerySession(tenant))
            {
                return session.Load<User>(id) != null;
            }
        }

        [ExposeAsTable("Can load an admin user document by id and tenant")]
        [return: Header("Returns a Document")]
        public bool CanLoadAdminById([Header("Tenant Id")] string tenant, string UserName)
        {
            var id = _users[UserName].Id;

            using (var session = _store.QuerySession(tenant))
            {
                return session.Load<AdminUser>(id) != null;
            }
        }

        [ExposeAsTable("Can load user documents by array of id's")]
        [return: Header("Results")]
        public string[] LoadByIdArray([Header("Tenant Id")]string tenant, string[] Names)
        {
            var ids = Names.Select(x => _users[x].Id).ToArray();
            using (var session = _store.QuerySession(tenant))
            {
                return session.LoadMany<User>(ids).Select(x => x.UserName).ToArray();
            }
        }

        [ExposeAsTable("Running Queries")]
        public string[] Querying([Header("Tenant Id")] string tenant,

            [SelectionValues("All Users", "Admin Users", "All User Names starting with 'A'", "Admin User Names starting with 'A'", "All Deleted Users", "Deleted Admin Users", "User names starting with 'A' via compiled query")]string Query)
        {
            using (var session = _store.OpenSession(tenant))
            {
                switch (Query)
                {
                    case "All Users":
                        return session.Query<User>().Select(x => x.UserName).ToArray();

                    case "Admin Users":
                        return session.Query<AdminUser>().Select(x => x.UserName).ToArray();

                    case "All User Names starting with 'A'":
                        return session.Query<User>().Where(x => x.UserName.StartsWith("A")).Select(x => x.UserName).ToArray();

                    case "Admin User Names starting with 'A'":
                        return session.Query<AdminUser>().Where(x => x.UserName.StartsWith("A")).Select(x => x.UserName).ToArray();

                    case "All Deleted Users":
                        return session.Query<User>().Where(x => x.IsDeleted()).Select(x => x.UserName).ToArray();

                    case "Deleted Admin Users":
                        return session.Query<AdminUser>().Where(x => x.IsDeleted()).Select(x => x.UserName).ToArray();

                    case "User names starting with 'A' via compiled query":
                        return session.Query(new UserNameStartsWithA()).ToArray();
                }

                throw new ArgumentOutOfRangeException(nameof(Query));
            }
        }
    }

    public class UserNameStartsWithA: ICompiledListQuery<User, string>
    {
        Expression<Func<IQueryable<User>, IEnumerable<string>>> ICompiledQuery<User, IEnumerable<string>>.QueryIs()
        {
            return query => query.Where(x => x.UserName.StartsWith("A")).Select(x => x.UserName);
        }
    }
}
