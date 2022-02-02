using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Marten.Testing.Documents;

namespace CommandLineRunner
{
    public class FindUserByAllTheThings: ICompiledQuery<User>
    {
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        public Expression<Func<IMartenQueryable<User>, User>> QueryIs()
        {
            return query =>
                query.Where(x => x.FirstName == FirstName && Username == x.UserName)
                    .Where(x => x.LastName == LastName)
                    .Single();
        }
    }
}
