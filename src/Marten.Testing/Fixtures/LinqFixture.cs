using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            // set the expressions
            expression(x => x.Number == 1);
            expression(x => x.Number > 3);
            expression(x => x.Number < 3);
            expression(x => x.Number <= 3);
            expression(x => x.Number >= 3);
            expression(x => x.Number != 3);

            expression(x => x.String == "A");
            expression(x => x.String != "A");

            AddSelectionValues("Expressions", _wheres.Keys.ToArray());
        }

        public override void SetUp()
        {
            _idToName.ClearAll();

            ConnectionSource.CleanBasicDocuments();
            _container = Container.For<DevelopmentModeRegistry>();
            _session = _container.GetInstance<IDocumentSession>();


        }

        private void expression(Expression<Func<Target, bool>> where)
        {
            var key = @where.As<LambdaExpression>().Body.ToString().TrimStart('(').TrimEnd(')');
            _wheres.Add(key, where);
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
                _.SetProperty(x => x.String).DefaultValue("Max");

                _.Do(t => _session.Store(t));
            }).AsTable("If the documents are").After(() => _session.SaveChanges());
        }

        [ExposeAsTable("Executing queries")]
        public void ExecutingQuery([SelectionList("Expressions")]string WhereClause, out ResultSet Results)
        {
            var expression = _wheres[WhereClause];
            var queryable = _session.Query<Target>().Where(expression);

            var sql = _session.As<DocumentSession>().BuildCommand<Target>(queryable).CommandText;
            Debug.WriteLine(sql);

            Results = new ResultSet(queryable.ToArray().Select(x => _idToName[x.Id]).ToArray());
        }
    }
}