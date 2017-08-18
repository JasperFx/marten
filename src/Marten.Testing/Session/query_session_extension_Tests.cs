using System.Linq;
using System.Threading;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Session
{
	public class query_session_extension_Tests : DocumentSessionFixture<NulloIdentityMap>
	{
		[Fact]
		public async void CanQueryByRuntimeType()
		{
			var user = new User();
			var company = new Company {Name = "Megadodo Publications"};
			theSession.Store(user);
			theSession.Store(company);
			theSession.SaveChanges();
			
			using (var session = theStore.OpenSession())
			{
                // SAMPLE: sample-query-type-parameter-overload
                dynamic userFromDb = session.Query(user.GetType(), "where id = ?", user.Id).FirstOrDefault();
                dynamic companyFromDb = (await session.QueryAsync(typeof(Company), "where id = ?", CancellationToken.None, company.Id)).FirstOrDefault();
                // ENDSAMPLE

				Assert.Equal(user.Id, userFromDb.Id);
				Assert.Equal(company.Name, companyFromDb.Name);
			}
		}
	}
}