using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public sealed class InExamples
    {
        public void in_example(IDocumentSession session)
        {
            // SAMPLE: in
            // Finds all SuperUser's whose role is either
            // Admin, Supervisor, or Director
            var users = session.Query<SuperUser>()
                .Where(x => x.Role.In("Admin", "Supervisor", "Director"));

            // ENDSAMPLE
        }

        public void in_list_example(IDocumentSession session)
        {
            // SAMPLE: in_list
            // Finds all SuperUser's whose role is either
            // Admin, Supervisor, or Director
            var listOfRoles = new List<string> {"Admin", "Supervisor", "Director"};

            var users = session.Query<SuperUser>()
                .Where(x => x.Role.In(listOfRoles));

            // ENDSAMPLE
        }

        public void in_array_example(IDocumentSession session)
        {
            // SAMPLE: in_array
            // Finds all UserWithNicknames's whose nicknames matches either "Melinder" or "Norrland"

            var nickNames = new[] {"Melinder", "Norrland"};

            var users = session.Query<UserWithNicknames>()
                .Where(x => x.Nicknames.In(nickNames));

            // ENDSAMPLE
        }
    }
}
