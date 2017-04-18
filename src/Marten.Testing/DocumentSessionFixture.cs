using System;
using System.Diagnostics;
using Marten.Services;

namespace Marten.Testing
{
    public abstract class DocumentSessionFixture<T> : IntegratedFixture where T : IIdentityMap
    {
        private readonly Lazy<IDocumentSession> _session; 
        

        protected DocumentSessionFixture()
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

        protected IDocumentSession theSession => _session.Value;

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