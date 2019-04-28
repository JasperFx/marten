using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_inner_query : IntegratedFixture
    {
        public class TypeWithInnerCollectionsWithJsonConverterAttribute
        {
            public Guid Id { get; set; }
            public string Flatten { get; set; }
            public string[] Array { get; set; }
            public List<string> List { get; set; }
            public IList<string> IList { get; set; }

            [JsonConverter(typeof(Marten.Util.CollectionToArrayJsonConverter))]
            public IEnumerable<string> Enumerable { get; set; }

            [JsonConverter(typeof(Marten.Util.CollectionToArrayJsonConverter))]
            public IEnumerable<string> IEnumerableFromArray { get; set; }

            [JsonConverter(typeof(Marten.Util.CollectionToArrayJsonConverter))]
            public IEnumerable<string> IEnumerbaleFromList { get; set; }

            [JsonConverter(typeof(Marten.Util.CollectionToArrayJsonConverter))]
            public ICollection<string> ICollection { get; set; }

            [JsonConverter(typeof(Marten.Util.CollectionToArrayJsonConverter))]
            public IReadOnlyCollection<string> IReadonlyCollection { get; set; }

            [JsonConverter(typeof(Marten.Util.CollectionToArrayJsonConverter))]
            public IReadOnlyCollection<TypeWithInnerCollectionsWithJsonConverterAttribute> IReadonlyCollectionOfInnerClasses { get; set; }

            public static TypeWithInnerCollectionsWithJsonConverterAttribute Create(params string[] array)
            {
                return new TypeWithInnerCollectionsWithJsonConverterAttribute()
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
                    IReadonlyCollectionOfInnerClasses = new List<TypeWithInnerCollectionsWithJsonConverterAttribute>
                    {
                        new TypeWithInnerCollectionsWithJsonConverterAttribute()
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
        };

        private static readonly TypeWithInnerCollectionsWithJsonConverterAttribute[] TestData = new TypeWithInnerCollectionsWithJsonConverterAttribute[]
        {
            TypeWithInnerCollectionsWithJsonConverterAttribute.Create("one", "two"),
            TypeWithInnerCollectionsWithJsonConverterAttribute.Create("two", "three"),
            TypeWithInnerCollectionsWithJsonConverterAttribute.Create("four", "five"),
        };

        private const string SearchPhrase = "two";

        public static readonly TheoryData<Expression<Func<TypeWithInnerCollectionsWithJsonConverterAttribute, bool>>> Predicates = new TheoryData<Expression<Func<TypeWithInnerCollectionsWithJsonConverterAttribute, bool>>>
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
        public async Task having_type_with_CollectionToArrayJsonConverter_can_query_against_array_of_string(Expression<Func<TypeWithInnerCollectionsWithJsonConverterAttribute, bool>> predicate)
        {
            SetupTestData();

            using (var query = theStore.QuerySession())
            {
                var results = await query.Query<TypeWithInnerCollectionsWithJsonConverterAttribute>()
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