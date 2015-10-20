using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using FubuCore;
using Marten.Linq;
using StoryTeller;
using StoryTeller.Grammars.Tables;
using StructureMap;
using StructureMap.Util;

namespace Marten.Testing.Fixtures
{
    public class LinqFixture : Fixture
    {
        private IContainer _container;
        private readonly LightweightCache<Guid, string> _idToName = new LightweightCache<Guid, string>();
        private IDocumentSession _session;
        private readonly Dictionary<string, Expression<Func<Target, bool>>> _wheres = new Dictionary<string, Expression<Func<Target, bool>>>();

        public LinqFixture()
        {
            Title = "Linq Support";
        }

        public override void SetUp()
        {
            _idToName.ClearAll();

            ConnectionSource.CleanBasicDocuments();
            _container = Container.For<DevelopmentModeRegistry>();
            _session = _container.GetInstance<IDocumentSession>();

            // set the expressions
            expression(x => x.Number == 1);
            expression(x => x.Number > 3);
            expression(x => x.Number < 3);
            expression(x => x.Number <= 3);
            expression(x => x.Number >= 3);

            AddSelectionValues("Expressions", _wheres.Keys.ToArray());
        }

        private void expression(Expression<Func<Target, bool>> where)
        {
            _wheres.Add(where.ToString(), where);
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

                _.SetProperty(x => x.Number);

                _.Do(t => _session.Store(t));
            }).AsTable("If the documents are").After(() => _session.SaveChanges());
        }

        [ExposeAsTable("Executing queries")]
        public void ExecutingQuery([SelectionList("Expressions")]string WhereClause, out string Sql, out ResultSet Results)
        {
            var expression = _wheres[WhereClause];
            var queryable = _session.Query<Target>().Where(expression);

            Sql = queryable.As<MartenQueryable<Target>>().ToCommand().CommandText;

            Results = new ResultSet(queryable.ToArray().Select(x => _idToName[x.Id]).ToArray());
        }
    }
}