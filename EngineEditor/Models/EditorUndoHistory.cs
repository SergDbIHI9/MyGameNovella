using System;
using System.Collections.Generic;

namespace EngineEditor.Models;

public sealed class EditorUndoHistory<T>
{
    private readonly Stack<T> _undo = new();
    private readonly Stack<T> _redo = new();
    private readonly int _capacity;

    public EditorUndoHistory(int capacity = 100)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(T snapshot)
    {
        if (_undo.Count == _capacity)
        {
            var retained = _undo.ToArray();
            _undo.Clear();
            for (int i = retained.Length - 2; i >= 0; i--)
                _undo.Push(retained[i]);
        }
        _undo.Push(snapshot);
        _redo.Clear();
    }

    public bool TryUndo(T current, out T previous)
    {
        if (!_undo.TryPop(out previous!))
            return false;
        _redo.Push(current);
        return true;
    }

    public bool TryRedo(T current, out T next)
    {
        if (!_redo.TryPop(out next!))
            return false;
        _undo.Push(current);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
