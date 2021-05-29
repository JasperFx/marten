using Lamar;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.Testing
{



    public class MartenServiceCollectionExtensionsTests
    {
        // Using Lamar for testing this because of its diagnostics

        [Fact]
        public void add_marten_with_just_connection_string()
        {
            using (var container = Container.For(x =>
            {
                x.AddMarten(ConnectionSource.ConnectionString);
            }))
            {
                ShouldHaveAllTheExpectedRegistrations(container);
            }
        }

        [Fact]
        public void add_marten_by_store_options()
        {
            using (var container = Container.For(x =>
            {
                var options = new StoreOptions();
                options.Connection(ConnectionSource.ConnectionString);
                x.AddMarten(options);
            }))
            {
                ShouldHaveAllTheExpectedRegistrations(container);
            }
        }

        [Fact]
        public void add_marten_by_configure_lambda()
        {
            using (var container = Container.For(x =>
            {
                x.AddMarten(opts => opts.Connection(ConnectionSource.ConnectionString));
            }))
            {
                ShouldHaveAllTheExpectedRegistrations(container);
            }
        }

        [Fact]
        public void eager_initialization_of_the_store()
        {
            IDocumentStore store = null;
            using (var container = Container.For(x =>
            {
                store = x.AddMarten(ConnectionSource.ConnectionString)
                    .InitializeStore();
            }))
            {
                ShouldHaveAllTheExpectedRegistrations(container);

                container.GetInstance<IDocumentStore>().ShouldBeSameAs(store);
            }
        }

        [Fact]
        public void use_custom_factory_by_type()
        {
            using (var container = Container.For(x =>
            {
                x.AddMarten(ConnectionSource.ConnectionString)
                    .BuildSessionsWith<SpecialBuilder>();
            }))
            {
                ShouldHaveAllTheExpectedRegistrations(container);

                var builder = container.GetInstance<ISessionFactory>()
                    .ShouldBeOfType<SpecialBuilder>();

                builder.BuiltQuery.ShouldBeTrue();
                builder.BuiltSession.ShouldBeTrue();
            }
        }

        [Fact]
        public void can_vary_the_scope_of_the_builder()
        {
            using (var container = Container.For(x =>
            {
                x.AddMarten(ConnectionSource.ConnectionString)
                    .BuildSessionsPerScopeWith<SpecialBuilder>();
            }))
            {
                ShouldHaveAllTheExpectedRegistrations(container);

                container.Model.For<ISessionFactory>()
                    .Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);
            }
        }

        public class SpecialBuilder: ISessionFactory
        {
            private readonly IDocumentStore _store;

            public SpecialBuilder(IDocumentStore store)
            {
                _store = store;
            }

            public IQuerySession QuerySession()
            {
                BuiltQuery = true;
                return _store.QuerySession();
            }

            public bool BuiltQuery { get; set; }

            public IDocumentSession OpenSession()
            {
                BuiltSession = true;
                return _store.OpenSession();
            }

            public bool BuiltSession { get; set; }
        }

        private static void ShouldHaveAllTheExpectedRegistrations(Container container)
        {
            container.Model.For<IDocumentStore>().Default.Lifetime.ShouldBe(ServiceLifetime.Singleton);
            container.Model.For<IDocumentSession>().Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);
            container.Model.For<IQuerySession>().Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);

            container.GetInstance<IDocumentStore>().ShouldNotBeNull();
            container.GetInstance<IDocumentSession>().ShouldNotBeNull();
            container.GetInstance<IQuerySession>().ShouldNotBeNull();
        }


    }


}
