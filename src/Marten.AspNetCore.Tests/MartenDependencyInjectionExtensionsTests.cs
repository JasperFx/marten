using System;
using Marten.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marten.AspNetCore.Tests
{
	public sealed class MartenDependencyInjectionExtensionsTests
    {
		[Fact]
		public void CannotWireupMartenWithoutStoreConfig()
		{
			IServiceCollection serviceCollection = new ServiceCollection();

			// ReSharper disable once AssignNullToNotNullAttribute
			Assert.Throws<ArgumentNullException>(() => serviceCollection.UseMarten(null));
		}

		[Fact]
		public void StoreInitializationShouldBeEagerByDefault()
		{
			IServiceCollection serviceCollection = new ServiceCollection();

			var initialized = false;
			serviceCollection.UseMarten(c =>
			{
				initialized = true;
				c.Connection((string)null);
			});

			Assert.True(initialized);
		}

		[Fact]
		public void StoreCanBeInitializedLazily()
		{
			IServiceCollection serviceCollection = new ServiceCollection();

			var initialized = false;

			serviceCollection.UseMarten(c =>
			{
				initialized = true;
				c.Connection((string)null);
			}, true);

			var provider = serviceCollection.BuildServiceProvider();

			Assert.False(initialized);

			provider.GetService(typeof(IDocumentStore));

			Assert.True(initialized);
		}


		[Fact]
		public void StoreWiredAsSingletonByDefault()
		{
			IServiceCollection serviceCollection = new ServiceCollection();

			serviceCollection.UseMarten(c => c.Connection((string)null));

			var provider = serviceCollection.BuildServiceProvider();

			var store1 = provider.GetService(typeof(IDocumentStore));
			var store2 = provider.GetService(typeof(IDocumentStore));

			Assert.Same(store1, store2);
		}


		[Fact]
		public void CanWireupDocumentSessions()
		{
			IServiceCollection serviceCollection = new ServiceCollection();
			
			serviceCollection.UseMarten(c => c.Connection((string)null))
				.UseSessions();

			var provider = serviceCollection.BuildServiceProvider();

			using (var scope = provider.CreateScope())
			{
				var session1 = scope.ServiceProvider.GetService(typeof(IDocumentSession));
				var session2 = scope.ServiceProvider.GetService(typeof(IDocumentSession));

				Assert.Same(session1, session2);
			}		
		}

		[Fact]
		public void DocumentSessionsDefaultToScoped()
		{
			IServiceCollection serviceCollection = new ServiceCollection();

			serviceCollection.UseMarten(c => c.Connection((string) null))
				.UseSessions();

			var provider = serviceCollection.BuildServiceProvider();

			IDocumentSession session1;
			IDocumentSession session2;

			using (var scope = provider.CreateScope())
			{
				session1 = (IDocumentSession)scope.ServiceProvider.GetService(typeof(IDocumentSession));				
			}

			using (var scope = provider.CreateScope())
			{
				session2 = (IDocumentSession)scope.ServiceProvider.GetService(typeof(IDocumentSession));
			}

			Assert.NotSame(session1, session2);
			Assert.Throws<ObjectDisposedException>(() => session1.CreateBatchQuery());
		}

		[Fact]
		public void CanOverrideSessionScope()
		{
			IServiceCollection serviceCollection = new ServiceCollection();

			serviceCollection.UseMarten(c => c.Connection((string)null))
				.UseSessions(ServiceLifetime.Singleton);

			var provider = serviceCollection.BuildServiceProvider();

			object session1;
			object session2;

			using (var scope = provider.CreateScope())
			{
				session1 = scope.ServiceProvider.GetService(typeof(IDocumentSession));
			}

			using (var scope = provider.CreateScope())
			{
				session2 = scope.ServiceProvider.GetService(typeof(IDocumentSession));
			}

			Assert.Same(session1, session2);						
		}

		[Fact]
		public void CanWireupQuerySession()
		{
			IServiceCollection serviceCollection = new ServiceCollection();
			
			serviceCollection.UseMarten(c => c.Connection((string)null))
				.UseSessions();

			var provider = serviceCollection.BuildServiceProvider();

			using (var scope = provider.CreateScope())
			{
				var session1 = scope.ServiceProvider.GetService(typeof(IQuerySession));
				var session2 = scope.ServiceProvider.GetService(typeof(IQuerySession));
				var session3 = scope.ServiceProvider.GetService(typeof(IDocumentSession));

				Assert.Same(session1, session2);
				Assert.NotSame(session2, session3);
			}
		}

		[Fact]
		public void CanWireupEventStore()
		{
			IServiceCollection serviceCollection = new ServiceCollection();
			
			serviceCollection.UseMarten(c => c.Connection((string)null))
				.UseSessions();

			var provider = serviceCollection.BuildServiceProvider();

			using (var scope = provider.CreateScope())
			{
				var session1 = scope.ServiceProvider.GetService(typeof(IEventStore));
				var session2 = scope.ServiceProvider.GetService(typeof(IEventStore));				

				Assert.Same(session1, session2);				
			}
		}
	}
}
