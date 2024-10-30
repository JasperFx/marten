using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_1019_event_type_not_found_bad_exception_message: BugIntegrationContext
    {
        [Fact]
        public async Task unknown_type_should_report_type_name()
        {
            var streamGuid = Guid.Parse("378b8405-8cdc-40ef-bafa-2033cd3c43c3");
            var typeName = "Bug1019.Product, EventSourcingTests";
            var newTypeName = "Foo, Bar";
            using (var session = theStore.LightweightSession())
            {
                var product = new Bug1019.Product { Id = 1, Name = "prod1", Price = 108 };
                session.Events.Append(streamGuid, product);
                await session.SaveChangesAsync();
                var command = session.Connection.CreateCommand();
                command.CommandText = @"
update
	bugs.mt_events
set
	mt_dotnet_type = :newDotnetTypeName
	, type = :newTypeName
where
	mt_dotnet_type = :originalTypeName
";
                command.AddNamedParameter("newDotnetTypeName", newTypeName);
                command.AddNamedParameter("newTypeName", "foo");
                command.AddNamedParameter("originalTypeName", typeName);
                command.ExecuteNonQuery();
                var ex = await Assert.ThrowsAsync<UnknownEventTypeException>(async () => await session.Events.FetchStreamAsync(streamGuid));
                ex.Message.ShouldContain(newTypeName);
            }
        }

    }


}

namespace Bug1019
{
    public class Product
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public decimal Price { get; set; }
    }
}
