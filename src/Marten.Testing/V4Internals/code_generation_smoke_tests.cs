using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.CoreFunctionality;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Util;
using Marten.V4Internals;
using Npgsql;
using NSubstitute;
using Xunit;
using VersionTracker = Marten.V4Internals.VersionTracker;

namespace Marten.Testing.V4Internals
{
    public interface ICodeGenScenario
    {
        void Compile();
        void CanGetIdentity();

        void HasQueryOnlyStorage();
        void HasLightweightStorage();
        void HasIdentityMapStorage();
        void HasDirtyTrackingStorage();

        void CanStoreLightweight();
        void CanStoreIdentityMap();
        //void CanStoreDirtyChecking();
        void CanEjectLightweight();
        void CanEjectIdentityMap();

        void CanBuildUpsertOperation();
        void CanBuildUpdateOperation();
        void CanBuildInsertOperation();
        void CanBuildOverwriteOperation();

        void CanBuildDeleteByDocument();

        void CanBuildDeleteByWhere();

        void CanBuildSelectors();

        void CanBuildBulkLoader();

    }

    public class DocWithVersionField
    {
        public Guid Id { get; set; }

        [Version] public Guid Version;
    }

    public class DocWithVersionProperty
    {
        public Guid Id { get; set; }

        [Version] public Guid Version { get; set; }
    }

    public class StubMartenSession: IMartenSession
    {
        public ISerializer Serializer { get; } = new JsonNetSerializer();
        public Dictionary<Type, object> ItemMap { get; } = new Dictionary<Type, object>();
        public ITenant Tenant { get; } = Substitute.For<ITenant>();
        public VersionTracker Versions { get; } = new VersionTracker();
        public Task<T> ExecuteQuery<T>(IQueryHandler<T> handler, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public T ExecuteQuery<T>(IQueryHandler<T> handler)
        {
            throw new NotImplementedException();
        }

        public IQueryable CreateQuery(Expression expression)
        {
            throw new NotImplementedException();
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            throw new NotImplementedException();
        }

        public object Execute(Expression expression)
        {
            throw new NotImplementedException();
        }

        public TResult Execute<TResult>(Expression expression)
        {
            throw new NotImplementedException();
        }
    }

    public class CodeGenScenario<T> : ICodeGenScenario where T : new()
    {
        public string Description { get; }
        public DocumentMapping<T> Mapping { get; }
        public StoreOptions Options { get; }

        public CodeGenScenario(string description, DocumentMapping<T> mapping, StoreOptions options)
        {
            Description = description;
            Mapping = mapping;
            Options = options;

            Document = new T();
        }

        public T Document { get; set; }

        public override string ToString()
        {
            return Description;
        }

        public void Compile()
        {
            CreateSlot();
        }

        public void CanGetIdentity()
        {
            var storage = CreateSlot().QueryOnly;
            var identityMethod = storage.GetType().GetMethod("Identity");
            identityMethod.Invoke(storage, new object[] {Document});
        }

        public void HasQueryOnlyStorage()
        {
            CreateSlot().QueryOnly.ShouldNotBeNull();
        }

        public void HasLightweightStorage()
        {
            CreateSlot().Lightweight.ShouldNotBeNull();
        }

        public void HasIdentityMapStorage()
        {
            CreateSlot().IdentityMap.ShouldNotBeNull();
        }

        public void HasDirtyTrackingStorage()
        {
            CreateSlot().DirtyTracking.ShouldNotBeNull();
        }

        public void CanStoreLightweight()
        {
            CreateSlot().Lightweight.Store(new StubMartenSession(), Document);
            CreateSlot().Lightweight.Store(new StubMartenSession(), Document, Guid.NewGuid());
        }

        public void CanEjectLightweight()
        {
            var session = new StubMartenSession();
            CreateSlot().Lightweight.Store(session, Document);
            CreateSlot().Lightweight.Eject(session, Document);
        }

        public void CanEjectIdentityMap()
        {
            var session = new StubMartenSession();
            CreateSlot().IdentityMap.Store(session, Document);
            CreateSlot().IdentityMap.Eject(session, Document);
        }

        public void CanBuildUpsertOperation()
        {
            var slot = CreateSlot();

            slot.Lightweight.Upsert(Document, new StubMartenSession()).ShouldNotBeNull();
            slot.IdentityMap.Upsert(Document, new StubMartenSession()).ShouldNotBeNull();
        }

        public void CanBuildUpdateOperation()
        {
            var slot = CreateSlot();

            slot.Lightweight.Update(Document, new StubMartenSession()).ShouldNotBeNull();
            slot.IdentityMap.Update(Document, new StubMartenSession()).ShouldNotBeNull();
        }

        public void CanBuildInsertOperation()
        {
            var slot = CreateSlot();

            slot.Lightweight.Insert(Document, new StubMartenSession()).ShouldNotBeNull();
            slot.IdentityMap.Insert(Document, new StubMartenSession()).ShouldNotBeNull();
        }

        public void CanBuildOverwriteOperation()
        {
            if (!Mapping.UseOptimisticConcurrency) return;

            var slot = CreateSlot();

            slot.Lightweight.Insert(Document, new StubMartenSession()).ShouldNotBeNull();
            slot.IdentityMap.Insert(Document, new StubMartenSession()).ShouldNotBeNull();
        }

        public void CanBuildDeleteByDocument()
        {
            var slot = CreateSlot();
            slot.Lightweight.DeleteForDocument(Document).ShouldNotBeNull();
            slot.IdentityMap.DeleteForDocument(Document).ShouldNotBeNull();
        }

        public void CanBuildDeleteByWhere()
        {
            var slot = CreateSlot();
            slot.Lightweight.DeleteForWhere(new WhereFragment("1 = 1")).ShouldNotBeNull();
            slot.IdentityMap.DeleteForWhere(new WhereFragment("1 = 1")).ShouldNotBeNull();
        }

        public void CanBuildSelectors()
        {
            var slot = CreateSlot();
            slot.QueryOnly.BuildSelector(new StubMartenSession()).ShouldNotBeNull();
            slot.Lightweight.BuildSelector(new StubMartenSession()).ShouldNotBeNull();
            slot.IdentityMap.BuildSelector(new StubMartenSession()).ShouldNotBeNull();
        }

        public void CanBuildBulkLoader()
        {
            CreateSlot().BulkLoader.ShouldNotBeNull();
        }


        public void CanStoreIdentityMap()
        {
            CreateSlot().IdentityMap.Store(new StubMartenSession(), Document);
            CreateSlot().IdentityMap.Store(new StubMartenSession(), Document, Guid.NewGuid());
        }

        private StorageSlot<T> CreateSlot()
        {
            var builder = new DocumentStorageBuilder(Mapping, Options);
            return builder.Generate<T>();
        }
    }

    public class code_generation_smoke_tests
    {
        private static readonly IList<ICodeGenScenario> _scenarios = new List<ICodeGenScenario>();

        protected static T Scenario<T>(string description, Func<StoreOptions, MartenRegistry.DocumentMappingExpression<T>> configuration = null) where T : new()
        {
            var options = new StoreOptions();
            var expression = configuration(options);

            options.ApplyConfiguration();

            var mapping = options.Storage.MappingFor(typeof(T)).As<DocumentMapping<T>>();
            var scenario = new CodeGenScenario<T>(description, mapping, options);

            _scenarios.Add(scenario);

            return scenario.Document;
        }


        static code_generation_smoke_tests()
        {
            Scenario("Guid Id", x => x.Schema.For<Target>());
            Scenario("Int Id", x => x.Schema.For<IntDoc>());
            Scenario("Long Id", x => x.Schema.For<LongDoc>());
            Scenario("String Id", x => x.Schema.For<StringDoc>())
                .Id = "foo";

            Scenario("Doc with Version Field, no optimistic concurrency", x => x.Schema.For<DocWithVersionField>());
            Scenario("Doc with Version Field, *with* optimistic concurrency", x => x.Schema.For<DocWithVersionField>().UseOptimisticConcurrency(true));


            Scenario("Doc with Version Property, no optimistic concurrency", x => x.Schema.For<DocWithVersionProperty>());
            Scenario("Doc with Version Property, *with* optimistic concurrency", x => x.Schema.For<DocWithVersionProperty>().UseOptimisticConcurrency(true));

        }

        public static IEnumerable<object[]> TestCases()
        {
            return testCases().ToArray();
        }

        private static IEnumerable<object[]> testCases()
        {
            var methods = typeof(ICodeGenScenario).GetMethods();

            foreach (var scenario in _scenarios)
            {
                foreach (var method in methods)
                {
                    yield return new object[]{scenario, method};
                }
            }
        }

        [Theory]
        [MemberData(nameof(TestCases))]
        public void compilation(ICodeGenScenario scenario, MethodInfo method)
        {
            method.Invoke(scenario, new object[0]);
        }




    }


}
