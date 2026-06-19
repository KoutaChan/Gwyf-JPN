using System;
using System.Collections.Generic;

namespace GwyfJpn.Plugin;

/// <summary>
/// Small bounded set used to suppress repeated unknown-log lines from frequently updated UI.
/// </summary>
internal sealed class LruSet
{
    private readonly int _capacity;
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);
    private readonly Queue<string> _queue = new();

    public LruSet(int capacity)
    {
        _capacity = capacity;
    }

    public bool Add(string value)
    {
        if (_set.Contains(value))
        {
            return false;
        }

        _set.Add(value);
        _queue.Enqueue(value);
        while (_queue.Count > _capacity)
        {
            _set.Remove(_queue.Dequeue());
        }

        return true;
    }
}
