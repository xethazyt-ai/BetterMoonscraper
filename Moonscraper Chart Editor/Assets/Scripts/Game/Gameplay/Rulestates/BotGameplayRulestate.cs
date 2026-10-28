using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BotGameplayRulestate : BaseGameplayRulestate
{
    public BotGameplayRulestate(MissFeedback missFeedbackFn) : base(missFeedbackFn)
    {

    }

    public void Update(float time, HitWindow<NoteHitKnowledge> hitWindow)
    {
        NoteHitKnowledge nextNoteToHit = hitWindow.oldestUnhitNote;
        while (nextNoteToHit != null)   // The bot is allowed to hit more than 1 note per frame
        { 
            NoteController nCon = nextNoteToHit.note.controller;
            if (nCon != null)
            {
                Vector3 notePosition = nCon.transform.position;
                Vector3 strikelinePosition = ChartEditor.Instance.visibleStrikeline.position;

                float visualOffset = Time.deltaTime / Globals.gameSettings.hyperspeed * Globals.gameSettings.gameSpeed;     // We want to hit it just before it crosses the strikeline. Looks a bit better. 
                bool belowStrikeLine = notePosition.y <= strikelinePosition.y + visualOffset;

                // Also hit once its time has come, even if it doesn't quite look it yet. A note right before another (an open
                // note 5 ticks before a chord) only stays in the window for a few milliseconds, so a frame where its drawn
                // position lagged its time was a miss, and the miss cut its sustain off.
                bool due = nextNoteToHit.note.time <= time;
                if (belowStrikeLine || due)
                {
                    HitNote(time, nextNoteToHit);
                    nextNoteToHit = hitWindow.oldestUnhitNote;
                }
                else
                {
                    nextNoteToHit = null;
                }
            }
            else
            {
                break;      // Its note isn't on screen (or was just deleted), so it can't be hit yet. Waiting here would freeze Moonscraper.
            }
        }

        // Note that having this logic here essentially gives the bot an infinite backend window in case of a framerate hiccup.
        int missCount = UpdateWindowExit(time, hitWindow);
        for (int i = 0; i < missCount; ++i)
        {
            if (stats.noteStreak > 0)
                Debug.Log("Bot missed due to note falling out of window. There may be more notes in the window thatn the framerate can handle");

            MissNote(time, GuitarNoteHitAndMissDetect.MissSubType.NoteMiss, null);
        }
    }

    void HitNote(float time, NoteHitKnowledge noteHitKnowledge)
    {
        base.HitNote(time, noteHitKnowledge);
    }

    void MissNote(float time, GuitarNoteHitAndMissDetect.MissSubType missSubType, GuitarNoteHitKnowledge noteHitKnowledge)
    {
        base.MissNote(time, missSubType == GuitarNoteHitAndMissDetect.MissSubType.NoteMiss, noteHitKnowledge);
    }
}
