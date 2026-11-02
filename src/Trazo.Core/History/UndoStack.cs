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

/// <summary>
/// Acción que libera recursos cuando sale del historial. <paramref name="applied"/> dice en qué estado queda para siempre:
/// true si se descarta hecha (lo antiguo se pierde: una capa eliminada ya no volverá) y false si se descarta deshecha
/// (lo nuevo se pierde: una capa añadida y deshecha ya no volverá).
/// </summary>
public interface IReleasableAction
{
    void Release(bool applied);
}

/// <param name="releaseApplied">Al descartarse hecha: libera lo que la acción quitó del documento.</param>
/// <param name="releaseUndone">Al descartarse deshecha: libera lo que la acción había añadido.</param>
public sealed class DelegateAction(string label, Action undo, Action redo, long bytes = 0, Action? releaseApplied = null, Action? releaseUndone = null)
    : IUndoableAction, IReleasableAction
{
    public string Label { get; } = label;
    public long Bytes { get; } = bytes;
    public void Undo() => undo();
    public void Redo() => redo();
    public void Release(bool applied) => (applied ? releaseApplied : releaseUndone)?.Invoke();
}

/// <summary>Varias acciones que se deshacen y rehacen como un solo paso.</summary>
public sealed class CompositeAction(string label, IReadOnlyList<IUndoableAction> actions) : IUndoableAction, IReleasableAction, IDisposable
{
    public string Label { get; } = label;
    public IReadOnlyList<IUndoableAction> Actions { get; } = actions;
    public long Bytes { get; } = actions.Sum(a => a.Bytes);

    public void Undo()
    {
        for (var i = Actions.Count - 1; i >= 0; i--) Actions[i].Undo();
    }

    public void Redo()
    {
        foreach (var a in Actions) a.Redo();
    }

    public void Release(bool applied)
    {
        foreach (var a in Actions) (a as IReleasableAction)?.Release(applied);
    }

    public void Dispose()
    {
        foreach (var a in Actions) (a as IDisposable)?.Dispose();
    }
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

    private List<IUndoableAction>? _group;
    private string? _groupLabel;

    /// <summary>
    /// Agrupa en un solo paso todo lo que se registre hasta liberar el valor devuelto (p. ej. Transformar mueve los
    /// píxeles y la selección: antes eran dos pasos de deshacer; auditoría 2026-11-02).
    /// </summary>
    public IDisposable BeginGroup(string label)
    {
        if (_group is not null) return new GroupEnd(null); // anidado: se suma al grupo exterior
        _group = [];
        _groupLabel = label;
        return new GroupEnd(this);
    }

    private sealed class GroupEnd(UndoStack? owner) : IDisposable
    {
        private UndoStack? _owner = owner;

        public void Dispose()
        {
            var o = _owner;
            _owner = null;
            if (o?._group is not { } list) return;
            o._group = null;
            if (list.Count == 1) o.Push(list[0]);
            else if (list.Count > 1) o.Push(new CompositeAction(o._groupLabel!, list));
        }
    }

    /// <summary>Registra una acción ya aplicada.</summary>
    public void Push(IUndoableAction action)
    {
        if (_group is not null)
        {
            _group.Add(action);
            return;
        }
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
        try
        {
            node.Value.Undo();
        }
        catch
        {
            // la acción ya no está en ninguna pila: se descuenta su memoria (auditoría 2026-11-02). No se liberan sus
            // recursos de documento: tras un fallo a medias no se sabe si siguen en uso
            Bytes -= node.Value.Bytes;
            (node.Value as IDisposable)?.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
        _redo.Push(node.Value);
        Changed?.Invoke(this, EventArgs.Empty);
        return node.Value;
    }

    public IUndoableAction? Redo()
    {
        if (!_redo.TryPop(out var a)) return null;
        try
        {
            a.Redo();
        }
        catch
        {
            Bytes -= a.Bytes;
            (a as IDisposable)?.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
            throw;
        }
        _undo.AddLast(a);
        Changed?.Invoke(this, EventArgs.Empty);
        return a;
    }

    public void Clear()
    {
        foreach (var a in _undo) Release(a, applied: true);
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
            Release(a, applied: false);
        }
    }

    private void Trim()
    {
        while (_undo.Count > MaxSteps || (Bytes > MaxBytes && _undo.Count > 1))
        {
            var a = _undo.First!.Value;
            _undo.RemoveFirst();
            Bytes -= a.Bytes;
            Release(a, applied: true);
        }
    }

    private static void Release(IUndoableAction a, bool applied)
    {
        (a as IReleasableAction)?.Release(applied);
        (a as IDisposable)?.Dispose();
    }
}
