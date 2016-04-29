using System;
using System.Diagnostics;
using System.Linq;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Util;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Examples
{
    // SAMPLE: trade_document_type
    public class Trade
    {
        public int Id { get; set; }

        [Searchable]
        public double Value { get; set; }
    }
    // ENDSAMPLE

    public class DiagnosticsExamples
    {


        public void use_diagnostics()
        {
            // Marten is NOT coupled to StructureMap, but we 
            // use it in our test suite for convenience
            var container = Container.For<DevelopmentModeRegistry>();

            var store = container.GetInstance<IDocumentStore>();

            // SAMPLE: preview_storage_code
            // store is the active IDocumentStore
            var storageCodeFor = store.Diagnostics.DocumentStorageCodeFor<Trade>();
            Debug.WriteLine(storageCodeFor);
            // ENDSAMPLE

            // SAMPLE: preview_linq_command
            // store is the active IDocumentStore
            var queryable = store.QuerySession().Query<Trade>().Where(x => x.Value > 2000);
            var cmd = QueryableExtensions.ToCommand<Trade>(queryable, FetchType.FetchMany);

            Debug.WriteLine(cmd.CommandText);
            // ENDSAMPLE


            // SAMPLE: preview_linq_explain_plan
            // Explain() is an extension method off of IQueryable<T>
            var plan = QueryableExtensions.Explain<Trade>(queryable);
            Console.WriteLine($"NodeType: {plan.NodeType}");
            Console.WriteLine($"RelationName: {plan.RelationName}");
            Console.WriteLine($"Alias: {plan.Alias}");
            Console.WriteLine($"StartupCost: {plan.StartupCost}");
            Console.WriteLine($"TotalCost: {plan.TotalCost}");
            Console.WriteLine($"PlanRows: {plan.PlanRows}");
            Console.WriteLine($"PlanWidth: {plan.PlanWidth}");
            // ENDSAMPLE
        }

        public void pregenerate_storage_code()
        {
            // SAMPLE: pregenerate_storage_code
            using (var store = DocumentStore.For(_ =>
            {
                _.Connection("something");

                // Document types that are known upfront
                _.Schema.For<User>();
                _.Schema.For<Issue>();
            }))
            {
                store.Advanced.PrecompileAllStorage();
            }
            // ENDSAMPLE
        }

        public void use_request_count()
        {
            var container = Container.For<DevelopmentModeRegistry>();

            var store = container.GetInstance<IDocumentStore>();

            // SAMPLE: using_request_count
            using (var session = store.QuerySession())
            {
                var users = session.Query<User>().ToList();
                var count = session.Query<User>().Count();
                var any = session.Query<User>().Any();

                session.RequestCount.ShouldBe(3);
            }
            // ENDSAMPLE
        }
    }
}