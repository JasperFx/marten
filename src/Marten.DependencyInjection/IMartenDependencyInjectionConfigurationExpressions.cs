using Marten.Services;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Marten
{
	public interface IMartenDependencyInjectionConfigurationExpressions
	{
		/// <summary>
		/// Setup resolution of IDocumentSession, IQuerySession, IEventStore.
		/// </summary>
		/// <param name="sessionLifetime">Optional session lifetime, defaults to ServiceLifetime.Scoped.</param>
		/// <param name="options">Optional session options to use.</param>		
		IServiceCollection UseSessions(ServiceLifetime sessionLifetime = ServiceLifetime.Scoped, SessionOptions options = null);
	}
}