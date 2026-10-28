// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

#define GAMEPAD

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class Whammy : MonoBehaviour {
    public float keyShiftSpeed = 5;
    public float widthMultiplier = 1;
    public float whammyLerpSpeed = 20;

    LineRenderer lineRenderer;
    SetLineRendererPoints pointsController;

    AnimationCurve lineCurve;
    ChartEditor.State previousApplicationMode;

    public float desiredWhammy = GuitarInput.kNoWhammy;

    // Use this for initialization
    void Start () {
        lineRenderer = GetComponent<LineRenderer>();
        pointsController = GetComponent<SetLineRendererPoints>();
        lineCurve = lineRenderer.widthCurve;
        previousApplicationMode = ChartEditor.Instance.currentState;
    }
    
	// Update is called once per frame
	void Update () {
        if (transform.localScale.y > 0)
        {
            UpdateWhammy(desiredWhammy);

            lineRenderer.widthCurve = lineCurve;
        }
    }

    void OnEnable()
    {
        ResetWhammy();
    }

    void OnDisable()
    {
        ResetWhammy();
    }

    void UpdateWhammy(float desiredWhammy)
    {
        pointsController.UpdateLineRendererPoints();
        ShiftAnimationKeys(lineCurve, keyShiftSpeed * Time.deltaTime * ChartEditor.worldUnitsPerSecond / transform.localScale.y);

        float whammyVal = (GetLerpedWhammyVal(desiredWhammy) + 1) * widthMultiplier;
        AddStartKey(lineCurve, whammyVal + 1);
    }

    const float MinKeySpacing = 0.01f;

    // Long sustains move their keys only a little each frame, so a new key every frame piled up hundreds of them.
    // While the newest key is still close to the start it takes the new value instead.
    static void AddStartKey(AnimationCurve lineCurve, float value)
    {
        Keyframe[] keys = lineCurve.keys;
        if (keys.Length > 0 && keys[0].time < MinKeySpacing)
            lineCurve.MoveKey(0, new Keyframe(keys[0].time, value));
        else
            lineCurve.AddKey(new Keyframe(0, value));
    }

    void ResetWhammy()
    {
        // Remove all whammy animation and reset
        if (lineRenderer && lineCurve != null)
        {
            Keyframe[] defaultKeys = new Keyframe[] { new Keyframe(0, 1), new Keyframe(0.9f, 1) };

            lineCurve.keys = defaultKeys;
            lineRenderer.widthCurve = lineCurve;
            pointsController.SetPositionsMinimum();

            currentWhammyVal = GuitarInput.kNoWhammy;
        }

        desiredWhammy = GuitarInput.kNoWhammy;
    }

    static void ShiftAnimationKeys(AnimationCurve lineCurve, float shiftDistance)
    {
        // Read and write the keys once, as each read of AnimationCurve.keys copies them all
        Keyframe[] keys = lineCurve.keys;
        int kept = 0;
        for (int i = 0; i < keys.Length; ++i)
        {
            float keyTime = keys[i].time + shiftDistance;
            if (keyTime <= 1)
                keys[kept++] = new Keyframe(keyTime, keys[i].value);
        }

        if (kept != keys.Length)
            System.Array.Resize(ref keys, kept);
        lineCurve.keys = keys;
    }

    public void SetWidth(float lineWidth)
    {
        if (!lineRenderer)
            lineRenderer = GetComponent<LineRenderer>();

        lineRenderer.widthMultiplier = lineWidth;
    }

    float currentWhammyVal = GuitarInput.kNoWhammy;
    float GetLerpedWhammyVal(float desiredWhammy)
    {
        float rawVal = GuitarInput.kNoWhammy;
        rawVal = desiredWhammy;

        {
            if (rawVal > currentWhammyVal)
            {
                currentWhammyVal += whammyLerpSpeed * Time.deltaTime;
                if (currentWhammyVal > rawVal)
                    currentWhammyVal = rawVal;
            }
            else if (rawVal.Round(2) < currentWhammyVal)
            {
                currentWhammyVal -= whammyLerpSpeed * Time.deltaTime;
                if (currentWhammyVal < rawVal)
                    currentWhammyVal = rawVal;
            }
        }

        return currentWhammyVal;
    }
}
