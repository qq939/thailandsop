using System;
using System.Collections;
using System.Collections.Generic;

namespace VideoInferenceDemo;

public sealed class RingBuffer<T> : IReadOnlyList<T>
{
    private readonly T[] _buffer;
    private int _start;
    private int _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new T[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _start = 0;
        _count = 0;
    }

    public void Add(T item)
    {
        if (_count < _buffer.Length)
        {
            _buffer[(_start + _count) % _buffer.Length] = item;
            _count++;
            return;
        }

        _buffer[_start] = item;
        _start = (_start + 1) % _buffer.Length;
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var idx = (_start + index) % _buffer.Length;
            return _buffer[idx];
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
