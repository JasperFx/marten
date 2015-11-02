using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;

namespace Marten.Testing
{
    public class duplicate_fields_in_table_and_upsert_Tests
    {
        public void end_to_end()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                container.GetInstance<DocumentCleaner>().CompletelyRemove(typeof(User));

                var schema = container.GetInstance<IDocumentSchema>();
                schema.MappingFor(typeof(User)).DuplicatedFields.Add(DuplicatedField.For<User>(x => x.FirstName));

                var user1 = new User {FirstName = "Byron", LastName = "Scott"};
                using (var session = container.GetInstance<IDocumentSession>())
                {
                    session.Store(user1);
                    session.SaveChanges();
                }

                var runner = container.GetInstance<CommandRunner>();
                runner.QueryScalar<string>($"select first_name from mt_doc_user where id = '{user1.Id.ToString()}'")
                    .ShouldBe("Byron");
            }
        } 
    }
}