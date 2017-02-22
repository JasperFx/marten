using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Testing;
using StoryTeller;
using StoryTeller.Grammars.ObjectBuilding;
using StoryTeller.Grammars.Tables;
using StructureMap;

namespace Marten.Storyteller.Fixtures
{
    public class OrderingFixture : MartenFixture
    {
        private readonly Dictionary<string, Func<IQueryable<Target>, IQueryable>> _queries = new Dictionary<string, Func<IQueryable<Target>, IQueryable>>();

        public OrderingFixture()
        {
            Title = "Ordering";

            expression("OrderBy(x => x.String)", t => t.OrderBy(x => x.String));
            expression("OrderByDescending(x => x.String)", t => t.OrderByDescending(x => x.String));

            expression("OrderBy(x => x.Number).ThenBy(x => x.String)", t => t.OrderBy(x => x.Number).ThenBy(x => x.String));
            expression("OrderBy(x => x.Number).ThenByDescending(x => x.String)", t => t.OrderBy(x => x.Number).ThenByDescending(x => x.String));
            expression("OrderByDescending(x => x.Number).ThenBy(x => x.String)", t => t.OrderByDescending(x => x.Number).ThenBy(x => x.String));

            expression("OrderBy(x => x.String).Take(2)", t => t.OrderBy(x => x.String).Take(2));
            expression("OrderBy(x => x.String).Skip(2)", t => t.OrderBy(x => x.String).Skip(2));
            expression("OrderBy(x => x.String).Take(2).Skip(2)", t => t.OrderBy(x => x.String).Take(2).Skip(2));

            AddSelectionValues("Expressions", _queries.Keys.ToArray());
        }

        private void expression(string orderBy, Func<IQueryable<Target>, IQueryable> func)
        {
            _queries.Add(orderBy, func);
        }

        protected override void configureDocumentsAre(ObjectConstructionExpression<Target> _)
        {
            _.WithInput<string>("Name").Configure((target, name) =>
            {
                IdToName[target.Id] = name;
            }).Header("Document Name");

            _.SetProperty(x => x.Number).DefaultValue("1");
            _.SetProperty(x => x.Long).DefaultValue("1");
            _.SetProperty(x => x.String).DefaultValue("Max");
        }

        [ExposeAsTable("Executing queries")]
        public void ExecutingQuery([SelectionList("Expressions")]string Query, out ResultSet Results)
        {
            var queryable = _queries[Query](Session.Query<Target>()).As<MartenQueryable<Target>>();
            var sql = queryable.ToCommand(FetchType.FetchMany).CommandText;
            Debug.WriteLine(sql);

            Results = new ResultSet(queryable.ToArray().Select(x => IdToName[x.Id]).ToArray());
        }
    }
}