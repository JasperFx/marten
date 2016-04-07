using System;
using System.Globalization;
using System.Threading;

namespace Marten.Testing
{
    public class CultureScope : IDisposable
    {
        private CultureInfo _originalCulture;

        public CultureScope(string culture = "en-US")
        {
            _originalCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
        }
        public void Dispose()
        {
            Thread.CurrentThread.CurrentCulture = _originalCulture;
            Thread.CurrentThread.CurrentUICulture = _originalCulture;
        }
    }
}
