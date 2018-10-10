using System;
using System.Linq;
using System.Reflection;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public sealed class IsOneOfArray
    {
        public void is_one_of_array_example(IDocumentSession session)
        {
            // SAMPLE: is_one_of_array
            // Finds all UserWithNicknames's whose nicknames matches either "Melinder" or "Norrland"

            var nickNames = new[] {"Melinder", "Norrland"};

            var users = session.Query<UserWithNicknames>()
                .Where(x => x.Nicknames.Any(n => nickNames.Contains(n)));

            // ENDSAMPLE
        }
    }
}