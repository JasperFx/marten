using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class TypeWithInnerCollections
    {
        public Guid Id { get; set; }
        public string Flatten { get; set; }
        public string[] Array { get; set; }
        public List<string> List { get; set; }
        public IList<string> IList { get; set; }

        public IEnumerable<string> Enumerable { get; set; }

        public IEnumerable<string> IEnumerableFromArray { get; set; }

        public IEnumerable<string> IEnumerbaleFromList { get; set; }

        public ICollection<string> ICollection { get; set; }

        public IReadOnlyCollection<string> IReadonlyCollection { get; set; }

        public IReadOnlyCollection<TypeWithInnerCollections> IReadonlyCollectionOfInnerClasses { get; set; }

        public static TypeWithInnerCollections Create(params string[] array)
        {
            return new TypeWithInnerCollections()
            {
                Id = Guid.NewGuid(),
                Flatten = array.Aggregate((i, j) => i + j),
                Array = array,
                List = array.ToList(),
                IList = array.ToList(),
                Enumerable = array.AsEnumerable(),
                IEnumerableFromArray = array,
                IEnumerbaleFromList = array.ToList(),
                ICollection = array.ToList(),
                IReadonlyCollection = array.ToList(),
                IReadonlyCollectionOfInnerClasses = new List<TypeWithInnerCollections>
                    {
                        new TypeWithInnerCollections()
                        {
                            Id = Guid.NewGuid(),
                            Flatten = array.Aggregate((i, j) => i + j),
                            Array = array,
                            List = array.ToList(),
                            IList = array.ToList(),
                            Enumerable = array.AsEnumerable(),
                            IEnumerableFromArray = array,
                            ICollection = array.ToList(),
                            IEnumerbaleFromList = array.ToList(),
                            IReadonlyCollection = array.ToList(),
                        }
                    }
            };
        }
    }

    [ControlledQueryStoryteller]
    public class query_with_inner_query_with_global_CollectionStorage_WithArray : IntegratedFixture
    {
        private static readonly TypeWithInnerCollections[] TestData = new TypeWithInnerCollections[]
        {
            TypeWithInnerCollections.Create("one", "two"),
            TypeWithInnerCollections.Create("two", "three"),
            TypeWithInnerCollections.Create("four", "five"),
        };

        private const string SearchPhrase = "two";

        public static readonly TheoryData<Expression<Func<TypeWithInnerCollections, bool>>> Predicates = new TheoryData<Expression<Func<TypeWithInnerCollections, bool>>>
        {
            x => x.Array.Contains(SearchPhrase),
            x => x.Enumerable.Contains(SearchPhrase),
            x => x.IEnumerableFromArray.Contains(SearchPhrase),
            x => x.IEnumerbaleFromList.Contains(SearchPhrase),
            x => x.List.Contains(SearchPhrase),
            x => x.IList.Contains(SearchPhrase),
            x => x.ICollection.Contains(SearchPhrase),
            x => x.IReadonlyCollection.Contains(SearchPhrase),
            x => x.IReadonlyCollection.Where(e => e == SearchPhrase).Any(),
            x => x.IReadonlyCollectionOfInnerClasses.Where(e => e.Flatten == "onetwo").Any() || x.IReadonlyCollectionOfInnerClasses.Where(e => e.Flatten == "twothree").Any()
        };

        [Theory]
        [MemberData(nameof(Predicates))]
        public async Task having_store_options_with_CollectionStorage_AsArray_can_query_against_array_of_string(Expression<Func<TypeWithInnerCollections, bool>> predicate)
        {
            StoreOptions(options =>
            {
                options.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray);
            });
            SetupTestData();

            using (var query = theStore.QuerySession())
            {
                var results = await query.Query<TypeWithInnerCollections>()
                    .Where(predicate)
                    .ToListAsync();

                results.Count.ShouldBe(2);
                results.All(e => e.Enumerable.Contains(SearchPhrase)).ShouldBeTrue();
            }
        }

        private void SetupTestData()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(TestData);
                session.SaveChanges();
            }
        }
    }
}