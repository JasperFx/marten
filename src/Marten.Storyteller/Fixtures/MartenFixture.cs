using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Testing;
using StoryTeller;
using StoryTeller.Grammars.ObjectBuilding;
using StoryTeller.Grammars.Tables;
using StoryTeller.Model;
using StructureMap;

namespace Marten.Storyteller.Fixtures
{
    public abstract class MartenFixture : Fixture
    {
        protected readonly LightweightCache<Guid, string> IdToName = new LightweightCache<Guid, string>();

        protected MartenFixture()
        {
            var path = AppContext.BaseDirectory;
            while (!path.EndsWith("Marten.Storyteller"))
            {
                path = path.ParentDirectory();
            }

            var filename = GetType().Name + ".cs";
            CodeFile = path.AppendPath("Fixtures", filename);
        }

        internal string CodeFile { get; }

        public override void SetUp()
        {
            IdToName.ClearAll();

            Store = TestingDocumentStore.Basic().As<DocumentStore>();
            Store.Advanced.Clean.CompletelyRemoveAll();

            Session = Store.OpenSession();

        }

        protected IDocumentSession Session { get; private set; }

        protected IDocumentSchema Schema => Store.Schema;
        protected DocumentStore Store { get; private set; }

        public override void TearDown()
        {
            Session.Dispose();
            Store.Dispose();
        }

        public IGrammar TheDocumentsAre()
        {
            return CreateNewObject<Target>("Documents", _ =>
            {
                _.ObjectIs = c => Target.Random(true);

                _.WithInput<string>("Name").Configure((target, name) =>
                {
                    IdToName[target.Id] = name;
                }).Header("Document Name");

                configureDocumentsAre(_);

                _.Do(t =>
                {
                    t.Date = t.Date.ToUniversalTime();
                    Session.Store(t);
                });

            }).AsTable("If the documents are").After(() => Session.SaveChanges());
        }

        protected virtual void configureDocumentsAre(ObjectConstructionExpression<Target> _)
        {
            // do nothing
        }

        [FormatAs("Seed with {count} documents")]
        public void SeedDocuments(int count)
        {
            var targets = Target.GenerateRandomData(count);
            Store.BulkInsert(targets.ToArray());
        }

        internal void AddQueryListValues(string listName, IList<string> names)
        {
            var i = 0;
            var options = names.Select(x => new Option(x, (++i).ToString())).ToArray();

            Lists[listName].AddOptions(options);
        }
    }




}