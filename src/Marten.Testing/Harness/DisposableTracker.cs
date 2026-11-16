using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Marten.Testing.Harness
{
    /// <summary>
    /// The list of things a test context disposes when the test finishes. It is safe to add to
    /// from several threads and safe to dispose more than once, and one failing disposal does not
    /// stop the rest. See #5976.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The contexts used to keep a plain <c>List&lt;IDisposable&gt;</c>. A test that builds stores
    /// concurrently, such as Bug_1404's four parallel <c>SeparateStore()</c> calls, races
    /// <c>List.Add</c>. When one thread grows the backing array while another writes into the old
    /// one, the second item is lost but the count still goes up, which leaves a null slot. The
    /// foreach in <c>Dispose()</c> then threw a NullReferenceException at that slot, which replaced
    /// the test's own result with a harness failure and leaked every store after the null.
    /// </para>
    /// <para>
    /// Disposal failures are collected and rethrown after everything has been disposed, never
    /// swallowed and never logged instead. A store that fails to dispose is a real defect, and
    /// nothing reads a log here. xUnit reports a cleanup exception together with the test's own
    /// failure, so rethrowing keeps both visible.
    /// </para>
    /// </remarks>
    public sealed class DisposableTracker: IList<IDisposable>
    {
        private readonly object _locker = new();
        private readonly List<IDisposable> _inner = new();

        /// <summary>
        /// Dispose everything tracked so far and empty the list, in the order it was added. Null
        /// entries are skipped. Every disposable is attempted; afterwards one failure is rethrown
        /// as it was, and several are thrown together as an <see cref="AggregateException"/>.
        /// Concurrent or repeated calls dispose each item at most once, because each call takes the
        /// items out of the list before disposing them.
        /// </summary>
        public void DisposeAll()
        {
            IDisposable[] snapshot;
            lock (_locker)
            {
                snapshot = _inner.ToArray();
                _inner.Clear();
            }

            List<Exception> failures = null;
            foreach (var disposable in snapshot)
            {
                if (disposable == null) continue;

                try
                {
                    disposable.Dispose();
                }
                catch (Exception e)
                {
                    (failures ??= new List<Exception>()).Add(e);
                }
            }

            if (failures == null) return;

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            throw new AggregateException("More than one test disposable failed to dispose", failures);
        }

        public void Add(IDisposable item)
        {
            lock (_locker) _inner.Add(item);
        }

        public void Clear()
        {
            lock (_locker) _inner.Clear();
        }

        public bool Contains(IDisposable item)
        {
            lock (_locker) return _inner.Contains(item);
        }

        public void CopyTo(IDisposable[] array, int arrayIndex)
        {
            lock (_locker) _inner.CopyTo(array, arrayIndex);
        }

        public bool Remove(IDisposable item)
        {
            lock (_locker) return _inner.Remove(item);
        }

        public int Count
        {
            get
            {
                lock (_locker) return _inner.Count;
            }
        }

        public bool IsReadOnly => false;

        public int IndexOf(IDisposable item)
        {
            lock (_locker) return _inner.IndexOf(item);
        }

        public void Insert(int index, IDisposable item)
        {
            lock (_locker) _inner.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            lock (_locker) _inner.RemoveAt(index);
        }

        public IDisposable this[int index]
        {
            get
            {
                lock (_locker) return _inner[index];
            }
            set
            {
                lock (_locker) _inner[index] = value;
            }
        }

        /// <summary>
        /// Enumerates a snapshot, so adding while enumerating neither throws nor races.
        /// </summary>
        public IEnumerator<IDisposable> GetEnumerator()
        {
            IDisposable[] snapshot;
            lock (_locker) snapshot = _inner.ToArray();

            return ((IEnumerable<IDisposable>)snapshot).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
