using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services.Json;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_1219_ordering_by_attributes : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1219_ordering_by_attributes(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }

        public class Car
        {
            public Guid Id { get; set; }
            public Dictionary<string, string> Attributes = new Dictionary<string,string>();

            public Dictionary<int, int> Numbers = new Dictionary<int, int>();
        }

        [SerializerTypeTargetedFact(RunFor = SerializerType.Newtonsoft)]
        public void can_order_by_string_string_dictionaries()
        {
            var car1 = new Car {Attributes = {{"one", "5"},{"anumber", "5"},{"color", "red"}}};
            var car2 = new Car {Attributes = {{"anumber", "3"}, {"color", "green"}}};
            var car3 = new Car {Attributes = {{"anumber", "7"}, {"color", "blue"}}};
            var car4 = new Car {Attributes = {{"anumber", "4"}, {"color", "purple"}}};

            theSession.Store(car1, car2, car3, car4);
            theSession.SaveChanges();

            using var query = theStore.QuerySession();

            var cars = query.Query<Car>().OrderBy(x => x.Attributes["color"]).ToArray();

            cars[0].Id.ShouldBe(car3.Id);
            cars[1].Id.ShouldBe(car2.Id);
            cars[2].Id.ShouldBe(car4.Id);
            cars[3].Id.ShouldBe(car1.Id);

            var cars2 = query.Query<Car>().OrderBy(x => x.Attributes["anumber"]).ToArray();
            cars2[0].Id.ShouldBe(car2.Id);
        }

        [Fact]
        public void smoke_test_can_order_by_not_string_values_in_dictionary()
        {
            using var query = theStore.QuerySession();

            query.Logger = new TestOutputMartenLogger(_output);

            query.Query<Car>().OrderBy(x => x.Numbers[2]).ToList();
        }
    }
}
