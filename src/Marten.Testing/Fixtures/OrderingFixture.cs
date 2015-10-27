using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FubuCore;
using Marten.Linq;
using StoryTeller;
using StoryTeller.Grammars.Tables;
using StructureMap;
using StructureMap.Util;

namespace Marten.Testing.Fixtures
{
    public class OrderingFixture : Fixture
    {
        private IContainer _container;
        private readonly LightweightCache<Guid, string> _idToName = new LightweightCache<Guid, string>();
        private IDocumentSession _session;
        private readonly Dictionary<string, Func<IQueryable<Target>, IQueryable>> _queries = new Dictionary<string, Func<IQueryable<Target>, IQueryable>>();

        public OrderingFixture()
        {
            Title = "Ordering";

            expression("OrderBy(x => x.String)", t => t.OrderBy(x => x.String));
            expression("OrderByDescending(x => x.String)", t => t.OrderByDescending(x => x.String));

            expression("OrderBy(x => x.Number).ThenBy(x => x.String)", t => t.OrderBy(x => x.Number).ThenBy(x => x.String));
            expression("OrderBy(x => x.Number).ThenByDescending(x => x.String)", t => t.OrderBy(x => x.Number).ThenByDescending(x => x.String));
            expression("OrderByDescending(x => x.Number).ThenBy(x => x.String)", t => t.OrderByDescending(x => x.Number).ThenBy(x => x.String));

            AddSelectionValues("Expressions", _queries.Keys.ToArray());
        }

        private void expression(string orderBy, Func<IQueryable<Target>, IQueryable> func)
        {
            _queries.Add(orderBy, func);
        }

        public override void SetUp()
        {
            _idToName.ClearAll();

            ConnectionSource.CleanBasicDocuments();
            _container = Container.For<DevelopmentModeRegistry>();
            _session = _container.GetInstance<IDocumentSession>();


        }


        public override void TearDown()
        {
            _session.Dispose();
            _container.Dispose();
        }

        public IGrammar TheDocumentsAre()
        {
            return CreateNewObject<Target>("Documents", _ =>
            {
                _.WithInput<string>("Name").Configure((target, name) =>
                {
                    _idToName[target.Id] = name;
                }).Header("Document Name");

                _.SetProperty(x => x.Number).DefaultValue("1");
                _.SetProperty(x => x.Long).DefaultValue("1");
                _.SetProperty(x => x.String).DefaultValue("Max");

                _.Do(t => _session.Store(t));
            }).AsTable("If the documents are").After(() => _session.SaveChanges());
        }

        [ExposeAsTable("Executing queries")]
        public void ExecutingQuery([SelectionList("Expressions")]string Query, out ResultSet Results)
        {
            var queryable = _queries[Query](_session.Query<Target>()).As<MartenQueryable<Target>>();
            var sql = _container.GetInstance<MartenQueryExecutor>().BuildCommand(queryable).CommandText;
            Debug.WriteLine(sql);

            Results = new ResultSet(queryable.ToArray().Select(x => _idToName[x.Id]).ToArray());
        }
    }
}