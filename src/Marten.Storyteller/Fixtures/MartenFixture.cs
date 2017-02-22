using System;
using Baseline;
using Marten.Schema;
using Marten.Testing;
using StoryTeller;
using StoryTeller.Grammars.ObjectBuilding;
using StoryTeller.Grammars.Tables;
using StructureMap;

namespace Marten.Storyteller.Fixtures
{
    public abstract class MartenFixture : Fixture
    {
        protected readonly LightweightCache<Guid, string> IdToName = new LightweightCache<Guid, string>();
        private IContainer _container;
        private IDocumentSession _session;
        private IDocumentStore _store;

        public override void SetUp()
        {
            IdToName.ClearAll();

            _container = Container.For<DevelopmentModeRegistry>();
            _store = _container.GetInstance<IDocumentStore>();
            _store.Advanced.Clean.CompletelyRemoveAll();

            _session = _store.OpenSession();


        }

        protected IDocumentSession Session => _session;
        protected IDocumentSchema Schema => _store.Schema;
        protected IDocumentStore Store => _store;

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

        protected abstract void configureDocumentsAre(ObjectConstructionExpression<Target> _);
    }


}