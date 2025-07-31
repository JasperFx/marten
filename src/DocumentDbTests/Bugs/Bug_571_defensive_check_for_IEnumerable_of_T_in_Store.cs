using System;
using System.Collections;
using System.Collections.Generic;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_571_defensive_check_for_IEnumerable_of_T_in_Store: IntegrationContext
{
    [Fact]
    public void not_too_tight_in_the_validation()
    {
        using var session = theStore.LightweightSession();
        session.Store(new DocHolder());
    }

    public Bug_571_defensive_check_for_IEnumerable_of_T_in_Store(DefaultStoreFixture fixture) : base(fixture)
    {
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
