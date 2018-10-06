using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_inner_query : IntegratedFixture
    {
        public class TypeWithInnerCollections
        {
            public Guid Id { get; set; }
            public string[] Array { get; set; }
            public IEnumerable<string> Enumerable { get; set; }
            public IEnumerable<string> IEnumerableFromArray { get; set; }
            public IEnumerable<string> IEnumerbaleFromList { get; set; }
            public List<string> List { get; set; }
            public IList<string> IList { get; set; }
            public IReadOnlyCollection<string> IReadonlyCollection { get; set; }
            public IReadOnlyCollection<TypeWithInnerCollections> IReadonlyCollectionOfInnerClasses { get; set; }

            public static TypeWithInnerCollections Create(params string[] array)
            {
                return new TypeWithInnerCollections()
                {
                    Id = Guid.NewGuid(),
                    Array = array,
                    Enumerable = array.AsEnumerable(),
                    IEnumerableFromArray = array,
                    IEnumerbaleFromList = array.ToList(),
                    List = array.ToList(),
                    IList = array.ToList(),
                    IReadonlyCollection = array.ToList()
                };
            }
        }

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
            x => x.IReadonlyCollection.Contains(SearchPhrase),
            x => x.IReadonlyCollection.Where(e => e == SearchPhrase).Any()
        };

        public query_with_inner_query()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(TestData);
                session.SaveChanges();
            }
        }

        [Theory]
        [MemberData(nameof(Predicates))]
        public async Task can_query_against_array_of_string(Expression<Func<TypeWithInnerCollections, bool>> predicate)

        {
            StoreOptions(options => options.UseDefaultSerialization());

            using (var query = theStore.QuerySession())
            {
                var results = await query.Query<TypeWithInnerCollections>()
                    .Where(predicate)
                    .ToListAsync();

                results.All(e => e.Enumerable.Contains(SearchPhrase)).ShouldBeTrue();
            }
        }
    }
}