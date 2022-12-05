using System.Diagnostics;

namespace Marten.OpenTelemetry;
internal class DiagnosticSourceSubscriber: IDisposable, IObserver<DiagnosticListener>
{
    private readonly Func<string, IObserver<KeyValuePair<string, object?>>> listnerFactory;
    private readonly Func<DiagnosticListener, bool> diagnosticSourceFilter;
    private readonly Func<string, object?, object?, bool>? isEnabledFilter;
    private long disposed;
    private IDisposable? allSourcesSubscription;
    private readonly List<IDisposable> listenerSubscriptions;
    public DiagnosticSourceSubscriber(
        Func<string, IObserver<KeyValuePair<string, object?>>> listnerFactory,
        Func<DiagnosticListener, bool> diagnosticSourceFilter,
        Func<string, object?, object?, bool>? isEnabledFilter)
    {
        listenerSubscriptions = new List<IDisposable>();
        this.listnerFactory = listnerFactory;
        this.diagnosticSourceFilter = diagnosticSourceFilter;
        this.isEnabledFilter = isEnabledFilter;
    }

    public void Subscribe()
    {
        allSourcesSubscription ??= DiagnosticListener.AllListeners.Subscribe(this);
    }

    public void OnNext(DiagnosticListener value)
    {
        if ((Interlocked.Read(ref disposed) == 0) &&
            diagnosticSourceFilter(value))
        {
            var listener = listnerFactory(value.Name);
            var subscription = isEnabledFilter == null ?
                value.Subscribe(listener) :
                value.Subscribe(listener, isEnabledFilter);

            lock (listenerSubscriptions)
            {
                listenerSubscriptions.Add(subscription);
            }
        }
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Interlocked.CompareExchange(ref this.disposed, 1, 0) == 1)
        {
            return;
        }

        lock (listenerSubscriptions)
        {
            foreach (var listenerSubscription in listenerSubscriptions)
            {
                listenerSubscription?.Dispose();
            }

            listenerSubscriptions.Clear();
        }

        allSourcesSubscription?.Dispose();
        allSourcesSubscription = null;
    }
}

