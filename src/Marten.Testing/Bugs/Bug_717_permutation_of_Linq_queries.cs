using System.Collections.Generic;
using System.Linq;
using Marten.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_717_permutation_of_Linq_queries: IntegratedFixture
    {
        [Fact]
        public void do_not_blow_up()
        {
            var entityToStore = new MainEntity
            {
                Entity1 = new ChildEntity1 { StringValues = new List<string> { "item1", "item2" } },
                Entity2 = new ChildEntity2
                {
                    InnerEntities = new List<InnerEntity>
                    {
                        new InnerEntity {MyEnum = SomeEnums.TestEnum1},
                        new InnerEntity {MyEnum = SomeEnums.TestEnum2},
                        new InnerEntity {MyEnum = SomeEnums.TestEnum3}
                    }
                }
            };

            using (var session = theStore.LightweightSession())
            {
                //first store the item in the database
                session.Store(entityToStore);

                //now try to get the data back
                QueryStatistics stats;

                /*------------------------------------------------------------------*/
                //getting an Exception while trying to execute this query

                var entity1 = session.Query<MainEntity>().Stats(out stats).FirstOrDefault(t => t.Entity1.StringValues.Any());

                //Marten.MartenCommandException: 'Marten Command Failure:
                //select d.data, d.id, d.mt_version, count(1) OVER() as total_rows from public.mt_doc_mainentity as d where JSONB_ARRAY_LENGTH(COALESCE(case when data->>'Entity1'->'StringValues' is not null then data->'Entity1'->'StringValues' else '[]' end)) > 0 LIMIT 1
                //42883: operator does not exist: text -> unknown'
                /*------------------------------------------------------------------*/

                /*------------------------------------------------------------------*/
                //same issue here as well

                var entity2 = session.Query<MainEntity>().Stats(out stats).FirstOrDefault(t => t.Entity2.InnerEntities.Any());
                //Marten.MartenCommandException: 'Marten Command Failure:
                //select d.data, d.id, d.mt_version, count(1) OVER() as total_rows from public.mt_doc_mainentity as d where JSONB_ARRAY_LENGTH(COALESCE(case when data->>'Entity2'->'InnerEntities' is not null then data->'Entity2'->'InnerEntities' else '[]' end)) > 0 LIMIT 1
                //42883: operator does not exist: text -> unknown'

                /*------------------------------------------------------------------*/

                /*------------------------------------------------------------------*/
                //and this two fail as well

                //var entity3 = session.Query<MainEntity>().Stats(out stats).FirstOrDefault(t => t.Entity1.StringValues.Any(n => n == "item1"));
                var entity3 = session.Query<MainEntity>().Stats(out stats).FirstOrDefault(t => t.Entity1.StringValues.Contains("item1"));
                //System.NotSupportedException: 'Specified method is not supported.'

                var entity4 = session.Query<MainEntity>().Stats(out stats).FirstOrDefault(t => t.Entity2.InnerEntities.Any(n => n.MyEnum == SomeEnums.TestEnum1));
                //System.NotImplementedException: 'The method or operation is not implemented.'
                /*------------------------------------------------------------------*/
            }
        }

        public class MainEntity
        {
            public long Id { get; set; }
            public ChildEntity1 Entity1 { get; set; }
            public ChildEntity2 Entity2 { get; set; }
        }

        public class ChildEntity1
        {
            public List<string> StringValues { get; set; }
        }

        public class ChildEntity2
        {
            public List<InnerEntity> InnerEntities { get; set; }
        }

        public class InnerEntity
        {
            public SomeEnums MyEnum { get; set; }
        }

        public enum SomeEnums
        {
            TestEnum1,
            TestEnum2,
            TestEnum3
        }
    }
}
