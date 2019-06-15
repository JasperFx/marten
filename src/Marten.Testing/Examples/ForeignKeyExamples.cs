using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class ForeignKeyExamples
    {
        public void configuration()
        {
            // SAMPLE: configure-foreign-key
            var store = DocumentStore
                .For(_ =>
                     {
                         _.Connection("some database connection");

                         // In the following line of code, I'm setting
                         // up a foreign key relationship to the User document
                         _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
                     });
            // ENDSAMPLE

            //var sql = store.Schema.ToDDL();
            //Console.WriteLine(sql);
        }

        public void external_fkey()
        {
            // SAMPLE: configure-external-foreign-key
            var store = DocumentStore
                .For(_ =>
                     {
                         _.Connection("some database connection");

                         // Here we create a foreign key to table that is not
                         // created or managed by marten
                         _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugtracker", "bugs", "id");
                     });
            // ENDSAMPLE
        }

        public void cascade_deletes_1()
        {
            // SAMPLE: cascade_deletes_with_config_func
            var store = DocumentStore
                .For(_ =>
                     {
                         _.Connection("some database connection");

                         _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.CascadeDeletes = true);
                     });
            // ENDSAMPLE
        }
    }
}