using System;
using System.Linq;
using System.Threading.Tasks;
using Lamar;
using LamarCodeGeneration;
using Marten;
using Marten.Internal;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests
{
    public class using_multiple_document_stores_in_same_host : IDisposable
    {
        private readonly Container theContainer;

        // TODO -- need to register additional ICodeFileCollection for the new store
        // TODO -- chained option to add an async daemon for each store
        // TODO -- post-configure options
        // TODO -- LATER, chain IInitialData

        public using_multiple_document_stores_in_same_host()
        {
            theContainer = Container.For(services =>
            {
                // Mostly just to prove we can mix and match
                services.AddMarten(ConnectionSource.ConnectionString);

                services.AddMartenStore<IFirstStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "first_store";
                });

                services.AddMartenStore<ISecondStore>(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = "second_store";
                });
            });
        }

        [Fact]
        public void should_have_a_single_registration_for_each_secondary_stores()
        {
            theContainer.Model.HasRegistrationFor<IFirstStore>().ShouldBeTrue();
            theContainer.Model.HasRegistrationFor<ISecondStore>().ShouldBeTrue();
        }

        [Fact]
        public void should_have_a_single_ICodeFileCollection_registration_for_secondary_stores()
        {
            theContainer.Model.InstancesOf<ICodeFileCollection>()
                .Count(x => x.ImplementationType == typeof(SecondaryDocumentStores)).ShouldBe(1);
        }

        [Fact]
        public void can_build_both_stores()
        {
            theContainer.GetInstance<IFirstStore>().ShouldNotBeNull();
            theContainer.GetInstance<ISecondStore>().ShouldNotBeNull();
        }

        [Fact]
        public async Task use_the_generated_store()
        {
            var store = theContainer.GetInstance<IFirstStore>();
            using var session = store.LightweightSession();

            var target = Target.Random();
            session.Store(target);

            await session.SaveChangesAsync();

            using var query = store.QuerySession();
            var target2 = await query.LoadAsync<Target>(target.Id);
            target2.ShouldNotBeNull();
        }

        public void Dispose()
        {
            theContainer?.Dispose();
        }
    }

    public interface IFirstStore : IDocumentStore{}
    public interface ISecondStore : IDocumentStore{}
}
