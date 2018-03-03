using System;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class LongEnoughNameToCauseIdTruncation
    {
        public Guid Id { get; set; }
        public Guid WhyWouldYouNameSomethingThisLong { get; set; }
        public Guid ShortEnough { get; set; }
    }

    public class Bug_953_too_long_index_names : IntegratedFixture
    {
        [Fact]
        public void can_ensure_storage_with_index_id_greater_than_63_bytes()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<LongEnoughNameToCauseIdTruncation>().Index(x => x.WhyWouldYouNameSomethingThisLong);
                _.NameDataLength = 64;
            });


            Exception<PostgresqlIdentifierTooLongException>.ShouldBeThrownBy(() =>
            {
                theStore.Tenancy.Default.EnsureStorageExists(typeof(LongEnoughNameToCauseIdTruncation));
            });


            
        }
    }
}