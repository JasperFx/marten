using System.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Linq
{
	// Change type mapping to treat "unknown" PG types as jsonb -> null checks depths at arbitrary depths don't fail due to CAST
	public class IsNullNotNullArbitraryDepthTests : IntegrationContextWithIdentityMap<NulloIdentityMap>
	{
		class UserNested : User
		{
			public UserNested Nested { get; set; }
		}

		[Fact]
		public void CanQueryNullNotNullAtArbitraryDepth()
		{
			var user = new UserNested
			{
				Nested = new UserNested
				{
					Nested = new UserNested
					{
						Nested = new UserNested()
					}
				}
			};

			theSession.Store(user);

			theSession.SaveChanges();

			using (var s = theStore.QuerySession())
			{
				var notNull = s.Query<UserNested>().FirstOrDefault(x => x.Nested.Nested.Nested != null);
				var notNullAlso = s.Query<UserNested>().FirstOrDefault(x => x.Nested.Nested.Nested.Nested.Nested == null);
				var shouldBeNull = s.Query<UserNested>().FirstOrDefault(x => x.Nested.Nested.Nested == null);

				Assert.Equal(user.Id, notNull.Id);
				Assert.Equal(user.Id, notNullAlso.Id);
				Assert.Null(shouldBeNull);
			}
		}

        [Fact]
        public void UnknownPGTypesMapToJsonb()
        {
            var locator = JsonLocatorField.For<UserNested>(EnumStorage.AsInteger, Casing.Default, x => x.Nested);
            Assert.Equal("CAST(d.data ->> 'Nested' as jsonb)", locator.TypedLocator);
        }

        public IsNullNotNullArbitraryDepthTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
