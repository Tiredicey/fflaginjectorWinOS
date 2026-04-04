using System.Collections.Generic;
using System.Linq;

namespace FlagInjector;

sealed class UndoStack
{
    const int MaxDepth = 50;
    readonly List<FlagSnapshot[]> _undo = new(), _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(List<FlagEntry> flags)
    {
        _undo.Add(Snap(flags));
        if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public FlagSnapshot[]? Undo(List<FlagEntry> current)
    {
        if (_undo.Count == 0) return null;
        _redo.Add(Snap(current));
        var s = _undo[^1]; _undo.RemoveAt(_undo.Count - 1);
        return s;
    }

    public FlagSnapshot[]? Redo(List<FlagEntry> current)
    {
        if (_redo.Count == 0) return null;
        _undo.Add(Snap(current));
        var s = _redo[^1]; _redo.RemoveAt(_redo.Count - 1);
        return s;
    }

    static FlagSnapshot[] Snap(List<FlagEntry> flags) =>
        flags.Select(f => new FlagSnapshot(f.Name, f.Value, f.Type, f.Enabled, f.Mode, f.History.ToList())).ToArray();

    public record FlagSnapshot(string Name, string Value, FType Type, bool Enabled, ApplyMode Mode, List<FlagHistoryEntry> History);
}