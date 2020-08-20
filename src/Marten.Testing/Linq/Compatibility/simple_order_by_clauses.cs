using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Linq.Compatibility.Support;
using Xunit;

namespace Marten.Testing.Linq.Compatibility
{
    public class simple_order_by_clauses: LinqTestContext<DefaultQueryFixture, simple_order_by_clauses>
    {
        public simple_order_by_clauses(DefaultQueryFixture fixture) : base(fixture)
        {
        }

        static simple_order_by_clauses()
        {
            ordered(t => t.OrderBy(x => x.String));
            ordered(t => t.OrderByDescending(x => x.String));

            ordered(t => t.OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase));
            ordered(t => t.OrderByDescending(x => x.String, StringComparer.OrdinalIgnoreCase));

            ordered(t => t.OrderBy(x => x.Number).ThenBy(x => x.String));
            ordered(t => t.OrderBy(x => x.Number).ThenByDescending(x => x.String));
            ordered(t => t.OrderByDescending(x => x.Number).ThenBy(x => x.String));

            ordered(t => t.OrderBy(x => x.String).Take(2));
            ordered(t => t.OrderBy(x => x.String).Skip(2));
            ordered(t => t.OrderBy(x => x.String).Take(2).Skip(2));
        }

        [Fact]
        public void order_by_query_format()
        {
            using var session = Fixture.Store.QuerySession();
            Assert.EndsWith(" order by d.data ->> 'String'", session.Query<Target>().OrderBy(x => x.String).ToCommand().CommandText);
            Assert.EndsWith(" order by lower(d.data ->> 'String')", session.Query<Target>().OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase).ToCommand().CommandText);
            Assert.EndsWith(" order by lower(d.data ->> 'String'), lower(d.data ->> 'AnotherString') desc", session.Query<Target>().OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase).ThenByDescending(x => x.AnotherString, StringComparer.OrdinalIgnoreCase).ToCommand().CommandText);

            Assert.EndsWith(" order by d.data ->> 'String' desc", session.Query<Target>().OrderByDescending(x => x.String).ToCommand().CommandText);
            Assert.EndsWith(" order by lower(d.data ->> 'String') desc", session.Query<Target>().OrderByDescending(x => x.String, StringComparer.OrdinalIgnoreCase).ToCommand().CommandText);

            Assert.EndsWith(" order by d.data ->> 'String', d.data ->> 'AnotherString'", session.Query<Target>().OrderBy(x => x.String).ThenBy(x => x.AnotherString).ToCommand().CommandText);
            Assert.EndsWith(" order by lower(d.data ->> 'String') desc, lower(d.data ->> 'AnotherString') desc", session.Query<Target>().OrderByDescending(x => x.String, StringComparer.OrdinalIgnoreCase).ThenByDescending(x => x.AnotherString, StringComparer.OrdinalIgnoreCase).ToCommand().CommandText);
        }

        [Fact]
        public void order_by_query_results()
        {
            Fixture.Store.Advanced.Clean.CompletelyRemoveAll();

            using var session = Fixture.Store.OpenSession();

            var objects = new[]
            {
                new Target
                {
                    String = "Andreas", AnotherString = "a"
                },
                new Target
                {
                    String = "adam", AnotherString = "b"
                },
                new Target
                {
                    String = "Bertha", AnotherString = "c"
                },
                new Target
                {
                    String = "Bob", AnotherString = "e"
                },
                new Target
                {
                    String = "Bob", AnotherString = "f"
                },
                new Target
                {
                    String = "bertil", AnotherString = "e"
                }
            };
            session.StoreObjects(objects);
            session.SaveChanges();

            var ids = objects.Select(x => x.Id).ToArray();

            Assert.Equal(
                objects.OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AnotherString, StringComparer.Ordinal).Select(x => new { x = x.String, y = x.AnotherString }).ToList(),
                session.Query<Target>().Where(x => x.Id.In(ids)).OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AnotherString, StringComparer.Ordinal).Select(x => new { x = x.String, y = x.AnotherString }).ToList());

            Assert.Equal(
                objects.OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AnotherString, StringComparer.Ordinal).Select(x => new { x = x.String, y = x.AnotherString }).ToList(),
                session.Query<Target>().Where(x => x.Id.In(ids)).OrderBy(x => x.String, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AnotherString, StringComparer.Ordinal).Select(x => new { x = x.String, y = x.AnotherString }).ToList());

            Assert.Equal(
                objects.OrderByDescending(x => x.String, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AnotherString).Select(x => new { x = x.String, y = x.AnotherString }).ToList(),
                session.Query<Target>().Where(x => x.Id.In(ids)).OrderByDescending(x => x.String, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.AnotherString, StringComparer.InvariantCulture).Select(x => new { x = x.String, y = x.AnotherString }).ToList());
        }

        [Fact]
        public void order_by_with_non_string()
        {
            using var session = Fixture.Store.QuerySession();
            var ex = Assert.Throws<ArgumentException>(() => session.Query<Target>().OrderBy(x => x.Number, Comparer<int>.Default).ToCommand());
            Assert.Equal("Only strings are supported when providing order comparer", ex.Message);
        }

        [Fact]
        public void order_by_with_custom_comparer()
        {
            using var session = Fixture.Store.QuerySession();
            var ex = Assert.Throws<ArgumentException>(() => session.Query<Target>().OrderBy(x => x.String, Comparer<string>.Create((s, s1) => 0)).ToCommand());
            Assert.Equal("Only standard StringComparer static comparer members are allowed as comparer", ex.Message);
        }
    }
}
