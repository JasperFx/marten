using System;
using Marten.Services;

namespace Marten.Testing.Harness
{
    // Preferred name: IntegrationContextWithIdentityMap
    public class IntegrationContextWithIdentityMap<T>: IntegrationContext where T : IIdentityMap
    {
        private readonly Lazy<IDocumentSession> _session;


        public IntegrationContextWithIdentityMap(DefaultStoreFixture fixture) : base(fixture)
        {
            _session = new Lazy<IDocumentSession>(() =>
            {
                if (typeof(T) == typeof(NulloIdentityMap))
                {
                    return theStore.OpenSession(tracking:DocumentTracking.None);
                }

                if (typeof(T) == typeof(IdentityMap))
                {
                    return theStore.OpenSession();
                }

                return theStore.DirtyTrackedSession();
            });
        }

        protected override IDocumentSession theSession => _session.Value;


        public override void Dispose()
        {
            if (_session.IsValueCreated)
            {
                _session.Value.Dispose();
            }

            base.Dispose();
        }
    }
}
