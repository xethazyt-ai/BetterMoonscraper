// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

#define DEBUG_METHOD_CALL

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MoonscraperEngine
{
    public class CommandStack
    {
        List<ICommand> commands;
        int currentStackIndex;

        public bool isAtStart { get { return currentStackIndex < 0; } }
        public bool isAtEnd { get { return currentStackIndex >= commands.Count - 1; } }

        // Called before the command is invoked/revoked
        protected virtual void OnPush(ICommand command) { }
        protected virtual void OnPop(ICommand command) { }

        // Called after the command has been invoked/revoked. isRedo is true when re-pushing a previously popped command.
        protected virtual void OnPushed(ICommand command, bool isRedo) { }
        protected virtual void OnPopped(ICommand command) { }
        protected virtual void OnCleared() { }

        public CommandStack()
        {
            Clear();
        }

        public void Clear()
        {
            commands = new List<ICommand>();
            currentStackIndex = -1;
            OnCleared();
        }

        public void Push()
        {
            if (!isAtEnd)
            {
                OnPush(commands[++currentStackIndex]);
                commands[currentStackIndex].Invoke();
                OnPushed(commands[currentStackIndex], true);
            }
        }

        public void Push(ICommand command)
        {
#if DEBUG_METHOD_CALL
            System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace(true);
            System.Diagnostics.StackFrame frame = stackTrace.GetFrame(1);
            Debug.LogFormat("Command Stack Push: {0} in file {1} at line {2}", frame.GetMethod().Name, System.IO.Path.GetFileName(frame.GetFileName()), frame.GetFileLineNumber());
#endif
            OnPush(command);
            ResetTail();
            ++currentStackIndex;
            command.Invoke();
            commands.Add(command);
            OnPushed(command, false);
        }

        public void Pop()
        {
#if DEBUG_METHOD_CALL
            System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace(true);
            System.Diagnostics.StackFrame frame = stackTrace.GetFrame(1);
            Debug.LogFormat("Command Stack Pop: {0} in file {1} at line {2}", frame.GetMethod().Name, System.IO.Path.GetFileName(frame.GetFileName()), frame.GetFileLineNumber());
#endif
            if (!isAtStart)
            {
                ICommand command = commands[currentStackIndex--];
                OnPop(command);
                command.Revoke();
                OnPopped(command);
            }
        }

        public void ResetTail()
        {
            commands.RemoveRange(currentStackIndex + 1, commands.Count - (currentStackIndex + 1));
        }
    }
}
