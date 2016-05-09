using System;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class ForeignKeyExamples
    {
        public void configuration()
        {
            // SAMPLE: configure-foreign-key
var store = DocumentStore.For(_ =>
{
    _.Connection("some database connection");

    // In the following line of code, I'm setting
    // up a foreign key relationship to the User document
    _.Schema.For<Issue>().ForeignKey<User>(x => x.AssigneeId);
});
            // ENDSAMPLE

            var sql = store.Schema.ToDDL();
            Console.WriteLine(sql);

        }
    }
}