using System;
using Marten.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Marten
{
	public static class MartenDependencyInjectionExtensions
	{
		/// <summary>
		/// Registers Marten IDocumentStore in the <see cref="IServiceCollection" />.
		/// </summary>
		/// <remarks>The IDocumentStore is wired as singleton, pertaining to Marten best practices.</remarks>
		/// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
		/// <param name="configureStore">Store configuration to be applied to Marten.</param>
		/// <param name="lazyStoreInitialization">Initialize store upon first service resolution. Initialization is eager by default.</param>		
		/// <returns></returns>
		public static IMartenDependencyInjectionConfigurationExpressions UseMarten(this IServiceCollection serviceCollection, Action<StoreOptions> configureStore, bool lazyStoreInitialization = false)
		{
			if (configureStore == null)
			{
				throw new ArgumentNullException(nameof(configureStore));
			}

			void EagerPath()
			{
				var documentStore = DocumentStore.For(configureStore);
				serviceCollection.TryAdd(new ServiceDescriptor(typeof(IDocumentStore), documentStore));
			}

			void LazyPath()
			{
				serviceCollection.TryAdd(new ServiceDescriptor(typeof(IDocumentStore),
					_ => DocumentStore.For(configureStore), ServiceLifetime.Singleton));
			}

			if (lazyStoreInitialization)
			{
				LazyPath();
			}
			else
			{
				EagerPath();
			}

			return new MartenDependencyInjectionConfigurationExpressions(serviceCollection);
		}
	}
}
