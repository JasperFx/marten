using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{

	public interface IHasAddressID
	{
		long ID { get; set; }
		long AddressID { get; set; }
	}
	public class PersonAddress : IHasAddressID
	{
		public long ID { get; set; }
		public string LastName { get; set; }
		public long AddressID { get; set; }
	}
	public class CompanyAddress : IHasAddressID
	{
		public long ID { get; set; }
		public string Name { get; set; }
		public long AddressID { get; set; }
	}

	public class query_over_multiple_types : DocumentSessionFixture<NulloIdentityMap>
	{
		public query_over_multiple_types() {
			StoreOptions(_ => {
				_.Schema.For<PersonAddress>();
				_.Schema.For<CompanyAddress>();

				_.Connection(ConnectionSource.ConnectionString);
				_.AutoCreateSchemaObjects = AutoCreate.All;
			});
			var p1 = new PersonAddress { LastName = "LastName1", AddressID = 100 };
			var p2 = new PersonAddress { LastName = "LastName2", AddressID = 101 };
			var c1 = new CompanyAddress { Name = "Name1", AddressID = 101 };
			theSession.Store(p1, p2);
			theSession.Store(c1);

			theSession.SaveChanges();
		}

		[Fact]
		public void get_count() {
			theSession.Query<IHasAddressID>().Count(x => x.AddressID == 101).ShouldBe(2);
		}

		[Fact]
		public void get_where() {
			var list = theSession.Query<IHasAddressID>().Where(x => x.AddressID == 101).ToList();
			list.Count.ShouldBe(2);
			list.First().ShouldBeOfType<PersonAddress>();
			(list.First() as PersonAddress).LastName.ShouldBe("LastName2");
			list.Last().ShouldBeOfType<CompanyAddress>();
			(list.Last() as CompanyAddress).Name.ShouldBe("Name1");
		}


		[Fact]
		public async Task get_count_async() {
			var cnt = await theSession.Query<IHasAddressID>().CountAsync(x => x.AddressID == 101).ConfigureAwait(false);
			cnt.ShouldBe(2);
		}
	}


}