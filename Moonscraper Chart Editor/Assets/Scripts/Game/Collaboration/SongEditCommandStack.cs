// See LICENSE in project root for license information.

using System.Collections.Generic;
using MoonscraperEngine;

/// <summary>
/// The editor's main command stack. Behaves exactly like CommandStack, but records the low-level changes
/// each push, redo and pop makes to the song and reports them through onStackChanged.
/// </summary>
public class SongEditCommandStack : CommandStack
{
    public enum ChangeType
    {
        Pushed,     // A new command was pushed
        Redone,     // A previously popped command was pushed again
        Popped,     // A command was revoked, by undo or by a tool replacing its last command
        Cleared,    // History was cleared, such as when loading a song or switching charts
    }

    public struct StackChange
    {
        public ChangeType type;
        public ICommand command;
        public List<SongEditChange> changes;
    }

    public readonly Event<StackChange> onStackChanged = new Event<StackChange>();

    protected override void OnPush(ICommand command)
    {
        SongEditJournal.Begin();
    }

    protected override void OnPushed(ICommand command, bool isRedo)
    {
        Fire(isRedo ? ChangeType.Redone : ChangeType.Pushed, command, SongEditJournal.End());
    }

    protected override void OnPop(ICommand command)
    {
        SongEditJournal.Begin();
    }

    protected override void OnPopped(ICommand command)
    {
        Fire(ChangeType.Popped, command, SongEditJournal.End());
    }

    protected override void OnCleared()
    {
        SongEditJournal.DiscardUnattachedChanges();

        // Also called from the base constructor. Safe, as C# runs field initialisers before base constructors.
        Fire(ChangeType.Cleared, null, new List<SongEditChange>());
    }

    void Fire(ChangeType type, ICommand command, List<SongEditChange> changes)
    {
        onStackChanged.Fire(new StackChange { type = type, command = command, changes = changes });
    }
}
