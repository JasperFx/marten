using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public sealed class IsOneOfExamples
    {
        public void is_one_of_example(IDocumentSession session)
        {
            #region sample_is_one_of
            // Finds all SuperUser's whose role is either
            // Admin, Supervisor, or Director
            var users = session.Query<SuperUser>()
                .Where(x => x.Role.IsOneOf("Admin", "Supervisor", "Director"));

            #endregion sample_is_one_of
        }

        public void is_one_of_list_example(IDocumentSession session)
        {
            #region sample_is_one_of_list
            // Finds all SuperUser's whose role is either
            // Admin, Supervisor, or Director
            var listOfRoles = new List<string> {"Admin", "Supervisor", "Director"};

            var users = session.Query<SuperUser>()
                .Where(x => x.Role.IsOneOf(listOfRoles));

            #endregion sample_is_one_of_list
        }

        public void is_one_of_array_example(IDocumentSession session)
        {
            #region sample_is_one_of_array
            // Finds all UserWithNicknames's whose nicknames matches either "Melinder" or "Norrland"

            var nickNames = new[] {"Melinder", "Norrland"};

            var users = session.Query<UserWithNicknames>()
                .Where(x => x.Nicknames.IsOneOf(nickNames));

            #endregion sample_is_one_of_array
        }
    }
}
