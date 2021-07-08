using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_enums_Tests : IntegrationContext
    {
        [Fact]
        public void use_enum_values_with_jil_that_are_not_duplicated()
        {
            StoreOptions(_ => _.Serializer<TestsSerializer>());

            theSession.Store(new Target{Color = Colors.Blue, Number = 1});
            theSession.Store(new Target{Color = Colors.Red, Number = 2});
            theSession.Store(new Target{Color = Colors.Green, Number = 3});
            theSession.Store(new Target{Color = Colors.Blue, Number = 4});
            theSession.Store(new Target{Color = Colors.Red, Number = 5});
            theSession.Store(new Target{Color = Colors.Green, Number = 6});
            theSession.Store(new Target{Color = Colors.Blue, Number = 7});

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }

        [Fact]
        public void use_enum_values_with_newtonsoft_that_are_not_duplicated()
        {
            StoreOptions(_ => _.Serializer<JsonNetSerializer>());

            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });
            theSession.Store(new Target { Color = Colors.Red, Number = 5 });
            theSession.Store(new Target { Color = Colors.Green, Number = 6 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 7 });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }

        [Fact]
        public void use_enum_values_with_newtonsoft_that_are_not_duplicated_and_stored_as_strings()
        {
            StoreOptions(_ => _.Serializer(new JsonNetSerializer {EnumStorage = EnumStorage.AsString}));

            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });
            theSession.Store(new Target { Color = Colors.Red, Number = 5 });
            theSession.Store(new Target { Color = Colors.Green, Number = 6 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 7 });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }


        [Fact]
        public void use_enum_values_with_jil_that_are_duplicated()
        {
            StoreOptions(_ =>
            {
                _.Serializer<TestsSerializer>();
                _.Schema.For<Target>().Duplicate(x => x.Color);
            });

            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });
            theSession.Store(new Target { Color = Colors.Red, Number = 5 });
            theSession.Store(new Target { Color = Colors.Green, Number = 6 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 7 });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }

        [Fact]
        public void use_enum_values_with_newtonsoft_that_are_duplicated()
        {
            StoreOptions(_ =>
            {
                _.Serializer<JsonNetSerializer>();
                _.Schema.For<Target>().Duplicate(x => x.Color);
            });

            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });
            theSession.Store(new Target { Color = Colors.Red, Number = 5 });
            theSession.Store(new Target { Color = Colors.Green, Number = 6 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 7 });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }

        [Fact]
        public void use_enum_values_with_newtonsoft_that_are_duplicated_as_string_storage()
        {
            StoreOptions(_ =>
            {
                _.UseDefaultSerialization(EnumStorage.AsString);
                _.Schema.For<Target>().Duplicate(x => x.Color);
            });

            theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
            theSession.Store(new Target { Color = Colors.Red, Number = 2 });
            theSession.Store(new Target { Color = Colors.Green, Number = 3 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 4 });
            theSession.Store(new Target { Color = Colors.Red, Number = 5 });
            theSession.Store(new Target { Color = Colors.Green, Number = 6 });
            theSession.Store(new Target { Color = Colors.Blue, Number = 7 });

            theSession.SaveChanges();

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }




        [Fact]
        public void use_enum_values_with_jil_that_are_duplicated_with_bulk_import()
        {
            StoreOptions(_ =>
            {
                _.Serializer<TestsSerializer>();
                _.Schema.For<Target>().Duplicate(x => x.Color);
            });

            var targets = new Target[]
            {
                new Target {Color = Colors.Blue, Number = 1},
                new Target {Color = Colors.Red, Number = 2},
                new Target {Color = Colors.Green, Number = 3},
                new Target {Color = Colors.Blue, Number = 4},
                new Target {Color = Colors.Red, Number = 5},
                new Target {Color = Colors.Green, Number = 6},
                new Target {Color = Colors.Blue, Number = 7}
            };

            theStore.BulkInsert(targets);

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }

        [Fact]
        public void use_enum_values_with_newtonsoft_that_are_duplicated_with_bulk_import()
        {
            StoreOptions(_ =>
            {
                _.Serializer<JsonNetSerializer>();
                _.Schema.For<Target>().Duplicate(x => x.Color);
            });

            var targets = new Target[]
            {
                new Target {Color = Colors.Blue, Number = 1},
                new Target {Color = Colors.Red, Number = 2},
                new Target {Color = Colors.Green, Number = 3},
                new Target {Color = Colors.Blue, Number = 4},
                new Target {Color = Colors.Red, Number = 5},
                new Target {Color = Colors.Green, Number = 6},
                new Target {Color = Colors.Blue, Number = 7}
            };

            theStore.BulkInsert(targets);

            theSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
                .Select(x => x.Number)
                .ShouldHaveTheSameElementsAs(1, 4, 7);
        }


        public query_with_enums_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
