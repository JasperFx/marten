using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
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

            expression(x => x.Long == 1);
            expression(x => x.Long > 3);
            expression(x => x.Long < 3);
            expression(x => x.Long <= 3);
            expression(x => x.Long >= 3);
            expression(x => x.Long != 3);

            expression(x => x.String == "A");
            expression(x => x.String.Equals("a", StringComparison.OrdinalIgnoreCase));
            expression(x => string.Equals(x.String, "a", StringComparison.OrdinalIgnoreCase));
            expression(x => string.Equals("a", x.String, StringComparison.OrdinalIgnoreCase));
            expression(x => x.String.Equals("A", StringComparison.Ordinal));
            expression(x => x.String != "A");

            expression(x => x.String == "A" && x.Number == 1);
            expression(x => x.String == "A" || x.Number == 1);

            expression(x => x.String.Contains("B"));
            expression(x => x.String.Contains("b", StringComparison.OrdinalIgnoreCase));
            expression(x => x.String.StartsWith("Bar"), "x.String.StartsWith(\"Bar\")");
            expression(x => x.String.StartsWith("bar", StringComparison.OrdinalIgnoreCase), "x.String.StartsWith(\"bar\", StringComparison.OrdinalIgnoreCase)");
            expression(x => x.String.EndsWith("Foo"), "x.String.EndsWith(\"Foo\")");
            expression(x => x.String.EndsWith("foo", StringComparison.OrdinalIgnoreCase), "x.String.EndsWith(\"Foo\", StringComparison.OrdinalIgnoreCase)");

            expression(x => x.String == null);

            expression(x => x.Flag);
            expression(x => x.Flag == true);
            expression(x => !x.Flag, "!Flag");
            expression(x => x.Flag == false);

            expression(x => x.Inner.Flag, "Inner.Flag");
            expression(x => !x.Inner.Flag, "!Inner.Flag");
            expression(x => x.Inner.Flag == true);
            expression(x => x.Inner.Flag == false);

            expression(x => x.Double == 10);
            expression(x => x.Double != 10);
            expression(x => x.Double > 10);
            expression(x => x.Double < 10);
            expression(x => x.Double <= 10);
            expression(x => x.Double >= 10);

            expression(x => x.Decimal == 10);
            expression(x => x.Decimal != 10);
            expression(x => x.Decimal > 10);
            expression(x => x.Decimal < 10);
            expression(x => x.Decimal <= 10);
            expression(x => x.Decimal >= 10);

            var today = DateTime.Today;

            expression(x => x.Date == today, "x.Date == Today");
            expression(x => x.Date != today, "x.Date != Today");
            expression(x => x.Date > today, "x.Date > Today");
            expression(x => x.Date < today, "x.Date < Today");
            expression(x => x.Date >= today, "x.Date >= Today");
            expression(x => x.Date <= today, "x.Date <= Today");


            AddSelectionValues("Expressions", _wheres.Keys.ToArray());

            AddSelectionValues("Fields", typeof(Target).GetProperties().Where(x => TypeMappings.HasTypeMapping(x.PropertyType)).Select(x => x.Name).ToArray());
        }

        public override void SetUp()
        {
            _idToName.ClearAll();

            ConnectionSource.CleanBasicDocuments();
            _container = Container.For<DevelopmentModeRegistry>();
            _session = _container.GetInstance<IDocumentStore>().OpenSession();


        }

        private void expression(Expression<Func<Target, bool>> where, string key = null)
        {
            if (key.IsEmpty())
            {
                key = @where.As<LambdaExpression>().Body.ToString().TrimStart('(').TrimEnd(')').Replace(") AndAlso (", " && ").Replace(") OrElse (", " || ");
            }
            _wheres.Add(key, where);
        }

        public override void TearDown()
        {
            _session.Dispose();
            _container.Dispose();
        }

        [FormatAs("The field {field} is configured to be duplicated")]
        public void FieldIsDuplicated([SelectionList("Fields")] string field)
        {
            _container.GetInstance<IDocumentSchema>().MappingFor(typeof(Target)).As<DocumentMapping>().DuplicateField(field);
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
                _.SetProperty(x => x.Flag).DefaultValue("false");
                _.SetProperty(x => x.Double).DefaultValue("1");
                _.SetProperty(x => x.Decimal).DefaultValue("1");
                _.SetProperty(x => x.Date).DefaultValue("TODAY");

                _.WithInput<bool>("InnerFlag").Configure((target, flag) =>
                {
                    if (target.Inner == null)
                    {
                        target.Inner = new Target();
                    }

                    target.Inner.Flag = flag;


                });

                _.Do(t => _session.Store(t));
            }).AsTable("If the documents are").After(() => _session.SaveChanges());
        }

        [ExposeAsTable("Executing queries")]
        public void ExecutingQuery([SelectionList("Expressions"), Header("Where Clause")]string WhereClause, out ResultSet Results)
        {
            var expression = _wheres[WhereClause];
            var queryable = _session.Query<Target>().Where(expression);
            var sql = _container.GetInstance<MartenQueryExecutor>().BuildCommand<Target>(queryable).CommandText;
            Debug.WriteLine(sql);

            Results = new ResultSet(queryable.ToArray().Select(x => _idToName[x.Id]).ToArray());
        }
    }
}