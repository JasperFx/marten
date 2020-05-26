using Marten.Events;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marten.DependencyInjection
{
	internal sealed class MartenDependencyInjectionConfigurationExpressions : IMartenDependencyInjectionConfigurationExpressions
	{
		private readonly IServiceCollection serviceCollection;

		public MartenDependencyInjectionConfigurationExpressions(IServiceCollection serviceCollection)
		{
			this.serviceCollection = serviceCollection;
		}

		public IServiceCollection UseSessions(ServiceLifetime sessionLifetime = ServiceLifetime.Scoped, SessionOptions options = null)
		{
			if (options != null)
			{
				var sessionDescriptor =
					new ServiceDescriptor(
						typeof(IDocumentSession), c =>
						{
							var store = (IDocumentStore)c.GetService(typeof(IDocumentStore));
							return store.OpenSession(options);
						}, sessionLifetime);

				var querySessionDescriptor =
					new ServiceDescriptor(
						typeof(IQuerySession), c =>
						{
							var store = (IDocumentStore)c.GetService(typeof(IDocumentStore));							

							return store.QuerySession(options);
						}, sessionLifetime);				


				serviceCollection.TryAdd(sessionDescriptor);
				serviceCollection.TryAdd(querySessionDescriptor);
			}
			else
			{
				var sessionDescriptor = new ServiceDescriptor(
					typeof(IDocumentSession), c =>
					{
						var store = (IDocumentStore)c.GetService(typeof(IDocumentStore));
						return store.OpenSession();
					}, sessionLifetime);

				var querySessionDescriptor =
					new ServiceDescriptor(
						typeof(IQuerySession), c =>
						{
							var store = (IDocumentStore)c.GetService(typeof(IDocumentStore));							
							return store.QuerySession();
						}, sessionLifetime);


				serviceCollection.TryAdd(sessionDescriptor);
				serviceCollection.TryAdd(querySessionDescriptor);
			}

			var eventStoreDescriptor =
				new ServiceDescriptor(
					typeof(IEventStore), c =>
					{
						var session = (IDocumentSession)c.GetService(typeof(IDocumentSession));
						return session.Events;
					}, sessionLifetime);

			serviceCollection.TryAdd(eventStoreDescriptor);	

			return serviceCollection;
		}
	}
}
