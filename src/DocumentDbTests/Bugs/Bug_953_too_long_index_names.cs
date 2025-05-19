using System;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class LongEnoughNameToCauseIdTruncation
{
    public Guid Id { get; set; }
    public Guid WhyWouldYouNameSomethingThisLong { get; set; }
    public Guid ShortEnough { get; set; }
}

public class Bug_953_too_long_index_names: BugIntegrationContext
{
    [Fact]
    public void can_ensure_storage_with_index_id_greater_than_63_bytes()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<LongEnoughNameToCauseIdTruncation>().Index(x => x.WhyWouldYouNameSomethingThisLong);
            _.NameDataLength = 64;
        });

        Should.Throw<PostgresqlIdentifierTooLongException>(() =>
        {
            theStore.Tenancy.Default.Database.EnsureStorageExists(typeof(LongEnoughNameToCauseIdTruncation));
        });
    }

}
