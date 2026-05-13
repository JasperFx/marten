#nullable enable
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Marten.Services;

/// <summary>
/// Fixed-size pooled <see cref="IList{T}"/> over <see cref="ArrayPool{T}.Shared"/>. Used to
/// stage per-batch column arrays for Npgsql <c>NpgsqlDbType.Array | …</c> parameters without
/// allocating a fresh <c>T[]</c> per Save. The rented buffer's length is typically larger
/// than the requested count — <see cref="Count"/> reports the user-supplied length so Npgsql
/// (which reads from <see cref="ICollection{T}.Count"/> when binding via the generic
/// <see cref="IList{T}"/> path) sends exactly the right element count to the wire.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> The buffer must remain valid until Npgsql has finished writing the
/// parameter to the wire (synchronously during <c>ExecuteReader</c> /
/// <c>ExecuteReaderAsync</c>). Dispose AFTER the reader has consumed the parameter set —
/// typically in <c>Postprocess</c> / <c>PostprocessAsync</c>. Disposing earlier corrupts the
/// payload.
/// </para>
/// <para>
/// <b>Not thread-safe.</b> Caller owns the rental until <see cref="Dispose"/>.
/// </para>
/// </remarks>
internal sealed class PooledList<T>: IList<T>, IReadOnlyList<T>, IDisposable
{
    private T[]? _buffer;
    private int _count;

    public PooledList(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _buffer = count == 0 ? Array.Empty<T>() : ArrayPool<T>.Shared.Rent(count);
        _count = count;
    }

    public int Count => _count;

    public bool IsReadOnly => false;

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _buffer![index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            _buffer![index] = value;
        }
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public int IndexOf(T item)
    {
        if (_buffer is null || _count == 0) return -1;
        return Array.IndexOf(_buffer, item, 0, _count);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        if (_buffer is null) return;
        Array.Copy(_buffer, 0, array, arrayIndex, _count);
    }

    // Mutating IList<T> members aren't meaningful for a fixed-size pooled buffer — the
    // class is intentionally write-once-via-indexer. Callers staging Npgsql parameters
    // never need these.
    void ICollection<T>.Add(T item) => throw new NotSupportedException();
    void ICollection<T>.Clear() => throw new NotSupportedException();
    bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
    void IList<T>.Insert(int index, T item) => throw new NotSupportedException();
    void IList<T>.RemoveAt(int index) => throw new NotSupportedException();

    public Enumerator GetEnumerator() => new(_buffer, _count);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = null;
        var count = _count;
        _count = 0;

        if (buffer is null || buffer.Length == 0) return;

        // Clear reference slots so the pooled buffer doesn't pin the prior payload past
        // the return. Value-type buffers skip this — the trimmer / JIT folds the call away.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && count > 0)
        {
            Array.Clear(buffer, 0, count);
        }
        ArrayPool<T>.Shared.Return(buffer);
    }

    /// <summary>
    /// Struct enumerator so <c>foreach</c> over a <see cref="PooledList{T}"/> doesn't box —
    /// matters when Npgsql binds via <see cref="IEnumerable{T}"/> rather than the indexer.
    /// </summary>
    public struct Enumerator: IEnumerator<T>
    {
        private readonly T[]? _buffer;
        private readonly int _count;
        private int _index;

        internal Enumerator(T[]? buffer, int count)
        {
            _buffer = buffer;
            _count = count;
            _index = -1;
        }

        public T Current => _buffer![_index];

        object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _count) return false;
            _index = next;
            return true;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}
