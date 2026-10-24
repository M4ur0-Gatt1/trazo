namespace Trazo.Core.History;

public interface IUndoableAction
{
    string Label { get; }
    /// <summary>Memoria aproximada que retiene la acción.</summary>
    long Bytes { get; }
    void Undo();
    void Redo();
}

/// <summary>Acciones que se pueden fusionar con la anterior (p. ej. arrastrar un deslizador).</summary>
public interface IMergeableAction : IUndoableAction
{
    bool TryMerge(IUndoableAction next);
}

public sealed class DelegateAction(string label, Action undo, Action redo, long bytes = 0) : IUndoableAction
{
    public string Label { get; } = label;
    public long Bytes { get; } = bytes;
    public void Undo() => undo();
    public void Redo() => redo();
}

/// <summary>Historial con límite de pasos y de memoria. Libera las acciones descartadas.</summary>
public sealed class UndoStack(int maxSteps = 200, long maxBytes = 1L * 1024 * 1024 * 1024)
{
    private readonly LinkedList<IUndoableAction> _undo = new();
    private readonly Stack<IUndoableAction> _redo = new();

    public event EventHandler? Changed;

    public int MaxSteps { get; } = maxSteps;
    public long MaxBytes { get; } = maxBytes;
    public long Bytes { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public string? NextUndoLabel => _undo.Last?.Value.Label;
    public string? NextRedoLabel => _redo.TryPeek(out var a) ? a.Label : null;

    /// <summary>Registra una acción ya aplicada.</summary>
    public void Push(IUndoableAction action)
    {
        ClearRedo();
        if (_undo.Last?.Value is IMergeableAction m && m.TryMerge(action))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }
        _undo.AddLast(action);
        Bytes += action.Bytes;
        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Aplica la acción (Redo) y la registra.</summary>
    public void Execute(IUndoableAction action)
    {
        action.Redo();
        Push(action);
    }

    public IUndoableAction? Undo()
    {
        if (_undo.Last is not { } node) return null;
        _undo.RemoveLast();
        node.Value.Undo();
        _redo.Push(node.Value);
        Changed?.Invoke(this, EventArgs.Empty);
        return node.Value;
    }

    public IUndoableAction? Redo()
    {
        if (!_redo.TryPop(out var a)) return null;
        a.Redo();
        _undo.AddLast(a);
        Changed?.Invoke(this, EventArgs.Empty);
        return a;
    }

    public void Clear()
    {
        foreach (var a in _undo) Release(a);
        _undo.Clear();
        ClearRedo();
        Bytes = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ClearRedo()
    {
        while (_redo.TryPop(out var a))
        {
            Bytes -= a.Bytes;
            Release(a);
        }
    }

    private void Trim()
    {
        while (_undo.Count > MaxSteps || (Bytes > MaxBytes && _undo.Count > 1))
        {
            var a = _undo.First!.Value;
            _undo.RemoveFirst();
            Bytes -= a.Bytes;
            Release(a);
        }
    }

    private static void Release(IUndoableAction a) => (a as IDisposable)?.Dispose();
}
