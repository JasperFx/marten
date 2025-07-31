using Marten.Testing.Documents;
using Weasel.Postgresql;

namespace Marten.Testing.Examples;

public class ForeignKeyExamples
{
    public void configuration()
    {
        #region sample_configure-foreign-key
        var store = DocumentStore
            .For(_ =>
            {
                _.Connection("some database connection");

                // In the following line of code, I'm setting
                // up a foreign key relationship to the User document
                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
            });
        #endregion

        //var sql = store.Schema.ToDDL();
        //Console.WriteLine(sql);
    }

    public void external_fkey()
    {
        #region sample_configure-external-foreign-key
        var store = DocumentStore
            .For(_ =>
            {
                _.Connection("some database connection");

                // Here we create a foreign key to table that is not
                // created or managed by marten
                _.Schema.For<Issue>().ForeignKey(i => i.BugId, "bugtracker", "bugs", "id");
            });
        #endregion
    }

    public void cascade_deletes_with_config_func()
    {
        #region sample_cascade_deletes_with_config_func
        var store = DocumentStore
            .For(_ =>
            {
                _.Connection("some database connection");

                _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId, fkd => fkd.OnDelete = CascadeAction.Cascade);
            });
        #endregion
    }
}