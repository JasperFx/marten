using System;
using System.Collections;
using System.Collections.Generic;
using Baseline;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_571_defensive_check_for_IEnumerable_of_T_in_Store: IntegratedFixture
    {
        [Fact]
        public void does_not_allow_IEnumerable_of_T()
        {
            var users = new User[0].As<IEnumerable<User>>();

            using (var session = theStore.OpenSession())
            {
                Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
                {
                    session.Store(users);
                });
            }
        }

        [Fact]
        public void not_too_tight_in_the_validation()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new DocHolder());
            }
        }

        public class DocHolder: IEnumerable<User>
        {
            public Guid Id;

            private readonly IList<User> _users = new List<User>();

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public IEnumerator<User> GetEnumerator()
            {
                return _users.GetEnumerator();
            }
        }
    }
}
