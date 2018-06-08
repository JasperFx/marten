using System;
using Marten.Events;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1019_event_type_not_found_bad_exception_message : IntegratedFixture
    {
        [Fact]
        public void unknown_type_should_report_type_name()
        {
            var streamGuid = Guid.Parse("378b8405-8cdc-40ef-bafa-2033cd3c43c3");
            var typeName = "Marten.Testing.Bugs.Bug1019.Product, Marten.Testing";
            var newTypeName = "Foo, Bar";
            using (var session = theStore.OpenSession())
            {
                var product = new Bug1019.Product {Id = 1, Name = "prod1", Price = 108};
                session.Events.Append(streamGuid, product);
                session.SaveChanges();
                var command = session.Connection.CreateCommand();
                command.CommandText = @"
update 
    public.mt_events 
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
                Assert.Throws<UnknownEventTypeException>(() => session.Events.FetchStream(streamGuid)).Message.ShouldContain(newTypeName);
            }

        }

        [Fact]
        public void eventmapping_for_type_can_be_obtiained_via_event_type_name()
        {
            var customEventTypeName = "foo";
            StoreOptions(options =>
            {
                var eventMapping = options.Storage.EventMappingFor<Bug1019.Product>();
                eventMapping.EventTypeName = customEventTypeName;
            });
            using (var session = theStore.OpenSession())
            {
                var eventGraph = session.DocumentStore.Events;
                eventGraph.EventMappingFor<Bug1019.Product>().ShouldBeTheSameAs(eventGraph.EventMappingFor(customEventTypeName));

                // note doing the following first would not work
                eventGraph.EventMappingFor(customEventTypeName).ShouldBeTheSameAs(eventGraph.EventMappingFor<Bug1019.Product>());
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

}