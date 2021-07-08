using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1155_null_duplicate_fields: BugIntegrationContext
    {
        [Fact]
        public void when_enum_is_null_due_to_nullable_type()
        {
            StoreOptions(_ =>
            {
                _.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsInteger });
                _.Schema.For<Target>().Duplicate(t => t.NullableColor);
            });

            using (var session = theStore.OpenSession())
            {
                session.Store(new Target
                {
                    Number = 1,
                    NullableColor = null
                });

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Where(x => x.Number == 1)
                    .ToArray()
                    .Select(x => x.Number)
                    .ShouldHaveTheSameElementsAs(1);
            }
        }

        [Fact]
        public void when_enum_is_null_due_to_nesting()
        {
            StoreOptions(_ =>
            {
                _.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsInteger });
                _.Schema.For<Target>().Duplicate(t => t.Inner.Color);
            });

            using (var session = theStore.OpenSession())
            {
                session.Store(new Target
                {
                    Number = 1,
                    Inner = null
                });

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Where(x => x.Number == 1)
                    .ToArray()
                    .Select(x => x.Number)
                    .ShouldHaveTheSameElementsAs(1);
            }
        }

        [Fact]
        public void when_string_enum_is_null_due_to_nullable_type()
        {
            StoreOptions(_ =>
            {
                _.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsString });
                _.Schema.For<Target>().Duplicate(t => t.NullableColor);
            });

            using (var session = theStore.OpenSession())
            {
                session.Store(new Target
                {
                    Number = 1,
                    NullableColor = null
                });

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Where(x => x.Number == 1)
                    .ToArray()
                    .Select(x => x.Number)
                    .ShouldHaveTheSameElementsAs(1);
            }
        }

        [Fact]
        public void when_string_enum_is_null_due_to_nesting()
        {
            StoreOptions(_ =>
            {
                _.Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsString });
                _.Schema.For<Target>().Duplicate(t => t.Inner.Color);
            });

            using (var session = theStore.OpenSession())
            {
                session.Store(new Target
                {
                    Number = 1,
                    Inner = null
                });

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Where(x => x.Number == 1)
                    .ToArray()
                    .Select(x => x.Number)
                    .ShouldHaveTheSameElementsAs(1);
            }
        }

        [Fact]
        public void when_field_is_not_null_due_to_nesting()
        {
            StoreOptions(_ => _.Schema.For<Target>().Duplicate(t => t.Inner.Number));

            using (var session = theStore.OpenSession())
            {
                session.Store(new Target
                {
                    Number = 1,
                    Inner = new Target { Number = 2 }
                });

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Where(x => x.Number == 1)
                    .ToArray()
                    .Select(x => x.Number)
                    .ShouldHaveTheSameElementsAs(1);
            }
        }

        [Fact]
        public void when_field_is_null_due_to_nesting()
        {
            StoreOptions(_ => _.Schema.For<Target>().Duplicate(t => t.Inner.Number));

            using (var session = theStore.OpenSession())
            {
                session.Store(new Target
                {
                    Number = 1,
                    Inner = null
                });

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Where(x => x.Number == 1)
                    .ToArray()
                    .Select(x => x.Number)
                    .ShouldHaveTheSameElementsAs(1);
            }
        }

        [Fact]
        public void when_bulk_inserting_and_field_is_null_due_to_nesting()
        {
            StoreOptions(_ => _.Schema.For<Target>().Duplicate(t => t.Inner.Number));

            theStore.BulkInsertDocuments(new[]
            {
                new Target
                {
                    Number = 1,
                    Inner = null
                }
            });

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Where(x => x.Number == 1)
                    .ToArray()
                    .Select(x => x.Number)
                    .ShouldHaveTheSameElementsAs(1);
            }
        }

    }
}
