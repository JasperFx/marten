using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Testing.Harness;
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
        public int[] ArrayWithInt { get; set; }
        public List<int> ListWithInt { get; set; }
        public IList<int> IListWithInt { get; set; }

        public IEnumerable<int> EnumerableWithInt { get; set; }

        public IEnumerable<int> IEnumerableWithIntFromArray { get; set; }

        public IEnumerable<int> IEnumerbaleWithIntFromList { get; set; }

        public ICollection<int> ICollectionWithInt { get; set; }

        public IReadOnlyCollection<int> IReadonlyCollectionWithInt { get; set; }

        public IReadOnlyCollection<TypeWithInnerCollections> IReadonlyCollectionOfInnerClasses { get; set; }

        public static TypeWithInnerCollections Create(params int[] array)
        {
            var stringArray = array.Select(x => x.ToString()).ToArray();
            return new TypeWithInnerCollections()
            {
                Id = Guid.NewGuid(),
                Flatten = stringArray.Aggregate((i, j) => i + j),
                Array = stringArray,
                List = stringArray.ToList(),
                IList = stringArray.ToList(),
                Enumerable = stringArray.AsEnumerable(),
                IEnumerableFromArray = stringArray,
                IEnumerbaleFromList = stringArray.ToList(),
                ICollection = stringArray.ToList(),
                IReadonlyCollection = stringArray.ToList(),
                ArrayWithInt = array,
                ListWithInt = array.ToList(),
                IListWithInt = array.ToList(),
                EnumerableWithInt = array.AsEnumerable(),
                IEnumerableWithIntFromArray = array,
                IEnumerbaleWithIntFromList = array.ToList(),
                ICollectionWithInt = array.ToList(),
                IReadonlyCollectionWithInt = array.ToList(),
                IReadonlyCollectionOfInnerClasses = new List<TypeWithInnerCollections>
                    {
                        new TypeWithInnerCollections()
                        {
                            Id = Guid.NewGuid(),
                            Flatten = stringArray.Aggregate((i, j) => i + j),
                            Array = stringArray,
                            List = stringArray.ToList(),
                            IList = stringArray.ToList(),
                            Enumerable = stringArray.AsEnumerable(),
                            IEnumerableFromArray = stringArray,
                            IEnumerbaleFromList = stringArray.ToList(),
                            ICollection = stringArray.ToList(),
                            IReadonlyCollection = stringArray.ToList(),
                            ArrayWithInt = array,
                            ListWithInt = array.ToList(),
                            IListWithInt = array.ToList(),
                            EnumerableWithInt = array.AsEnumerable(),
                            IEnumerableWithIntFromArray = array,
                            IEnumerbaleWithIntFromList = array.ToList(),
                            ICollectionWithInt = array.ToList(),
                            IReadonlyCollectionWithInt = array.ToList(),
                        }
                    }
            };
        }
    }

    [ControlledQueryStoryteller]
    public class query_with_inner_query_with_global_CollectionStorage_WithArray: IntegrationContext
    {
        private static readonly TypeWithInnerCollections[] TestData = new TypeWithInnerCollections[]
        {
            TypeWithInnerCollections.Create(1, 2),
            TypeWithInnerCollections.Create(2, 3),
            TypeWithInnerCollections.Create(4, 5),
        };

        private const string SearchPhrase = "2";
        private const int IntSearchPhrase = 2;

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
            x => x.IReadonlyCollectionOfInnerClasses.Where(e => e.Flatten == "12").Any() || x.IReadonlyCollectionOfInnerClasses.Where(e => e.Flatten == "23").Any(),
            x => x.ArrayWithInt.Contains(IntSearchPhrase),
            x => x.EnumerableWithInt.Contains(IntSearchPhrase),
            x => x.IEnumerableWithIntFromArray.Contains(IntSearchPhrase),
            x => x.IEnumerbaleWithIntFromList.Contains(IntSearchPhrase),
            x => x.ListWithInt.Contains(IntSearchPhrase),
            x => x.IListWithInt.Contains(IntSearchPhrase),
            x => x.ICollectionWithInt.Contains(IntSearchPhrase),
            x => x.IReadonlyCollectionWithInt.Contains(IntSearchPhrase),
            x => x.IReadonlyCollectionWithInt.Where(e => e == IntSearchPhrase).Any(),
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

        public query_with_inner_query_with_global_CollectionStorage_WithArray(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
