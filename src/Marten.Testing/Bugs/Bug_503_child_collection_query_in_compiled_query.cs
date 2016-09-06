using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_503_child_collection_query_in_compiled_query : IntegratedFixture
    {

        [Fact] 
        public void try_to_query()
        {
            using (var session = theStore.OpenSession())
            {
                var outer = new Outer();
                outer.Inners.Add(new Inner() { Type = "T1", Value = "V11" });
                outer.Inners.Add(new Inner() { Type = "T1", Value = "V12" });
                outer.Inners.Add(new Inner() { Type = "T2", Value = "V21" });

                session.Store(outer);
                session.SaveChanges();
            }

            using (var session2 = theStore.OpenSession())
            {
                // This works
                var o1 = session2.Query<Outer>().FirstOrDefault(o => o.Inners.Any(i => i.Type == "T1" && i.Value == "V12"));
                o1.ShouldNotBeNull();

                var o2 = session2.Query(new FindOuterByInner("T1", "V12"));

                o2.ShouldNotBeNull();

                o2.Id.ShouldBe(o1.Id);
            }
        }





        public class Outer
        {
            public Guid Id { get; set; }

            public IList<Inner> Inners { get; } = new List<Inner>();
        }

        public class Inner
        {
            public string Type { get; set; }

            public string Value { get; set; }
        }

        public class FindOuterByInner : ICompiledQuery<Outer, Outer>
        {
            public string Type { get; private set; }

            public string Value { get; private set; }

            public FindOuterByInner(string type, string value)
            {
                this.Type = type;
                this.Value = value;
            }

            public Expression<Func<IQueryable<Outer>, Outer>> QueryIs()
            {
                return q => q.FirstOrDefault(o => o.Inners.Any(i => i.Type == Type && i.Value == Value));
            }
        }
    }
}