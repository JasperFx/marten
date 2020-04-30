using System;
using Xunit;

namespace Marten.Testing.Harness
{
    public abstract class StoreContext<T> : IClassFixture<T>, IDisposable where T : StoreFixture
    {
#if NET461
        private CultureInfo _originalCulture;
#endif

        private readonly T _fixture;

        protected StoreContext(T fixture)
        {
            _fixture = fixture;

#if NET461
            _originalCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
#endif
        }

        public virtual void Dispose()
        {
#if NET461
            Thread.CurrentThread.CurrentCulture = _originalCulture;
            Thread.CurrentThread.CurrentUICulture = _originalCulture;
#endif

            _session?.Dispose();
        }

        protected virtual DocumentStore theStore => _fixture.Store;

        private IDocumentSession _session;

        protected virtual IDocumentSession theSession
        {
            get
            {
                if (_session == null)
                {
                    _session = theStore.LightweightSession();
                }

                return _session;
            }
        }
    }
}
