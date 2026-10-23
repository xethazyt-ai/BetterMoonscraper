// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using MoonscraperEngine;

/// <summary>
/// Turns command stack activity into finished edits.
///
/// Many tools rewrite their own last command while the user is still dragging or holding keys
/// (sustain drags, star power length, BPM drags, eraser drags, keyboard note entry): they pop the last
/// command and push a replacement on every update. This holds the newest pushed command as "pending"
/// and only reports it once the user lets go of every key and mouse button, or another command is
/// pushed on top of it. A pending command that is popped is dropped without being reported, as its
/// changes have been fully reverted.
///
/// Every reported edit is the exact list of changes made to the song, so applying all reported edits
/// in order to a copy of the song reproduces the editor's song.
/// </summary>
public class SongEditGestureTracker : SystemManagerState.System
{
    public enum EditKind
    {
        Applied,    // A new edit
        Reverted,   // A previously reported edit was popped off the stack (undo, or a tool replacing it)
        Reapplied,  // A previously reverted edit was redone
    }

    public struct FinishedEdit
    {
        public EditKind kind;
        public ICommand command;
        public List<SongEditChange> changes;
    }

    public readonly Event<FinishedEdit> onEditFinished = new Event<FinishedEdit>();
    public readonly MoonscraperEngine.Event onHistoryCleared = new MoonscraperEngine.Event();

    ICommand m_pendingCommand = null;
    List<SongEditChange> m_pendingChanges = null;

    public SongEditGestureTracker(SongEditCommandStack commandStack)
    {
        commandStack.onStackChanged.Register(OnStackChanged);
    }

    public bool hasPendingEdit { get { return m_pendingCommand != null; } }

    public override void SystemUpdate()
    {
        if (hasPendingEdit && !Input.anyKey)
        {
            FinishPending();
        }
    }

    void OnStackChanged(in SongEditCommandStack.StackChange change)
    {
        switch (change.type)
        {
            case SongEditCommandStack.ChangeType.Pushed:
                FinishPending();
                m_pendingCommand = change.command;
                m_pendingChanges = change.changes;
                break;

            case SongEditCommandStack.ChangeType.Popped:
                if (hasPendingEdit && change.command == m_pendingCommand)
                {
                    // Never reported, and its changes have now been reverted. Nothing to tell anyone about.
                    m_pendingCommand = null;
                    m_pendingChanges = null;
                }
                else
                {
                    FinishPending();
                    Report(EditKind.Reverted, change.command, change.changes);
                }
                break;

            case SongEditCommandStack.ChangeType.Redone:
                FinishPending();
                Report(EditKind.Reapplied, change.command, change.changes);
                break;

            case SongEditCommandStack.ChangeType.Cleared:
                FinishPending();
                onHistoryCleared.Fire();
                break;
        }
    }

    public void FinishPending()
    {
        if (!hasPendingEdit)
            return;

        ICommand command = m_pendingCommand;
        List<SongEditChange> changes = m_pendingChanges;
        m_pendingCommand = null;
        m_pendingChanges = null;

        Report(EditKind.Applied, command, changes);
    }

    void Report(EditKind kind, ICommand command, List<SongEditChange> changes)
    {
        if (changes == null || changes.Count <= 0)
            return;

        onEditFinished.Fire(new FinishedEdit { kind = kind, command = command, changes = changes });
    }
}
