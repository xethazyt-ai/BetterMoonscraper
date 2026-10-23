// See LICENSE in project root for license information.

// Minimal stand-ins for the Unity engine and the editor classes the chart model and edit commands touch,
// so they can be compiled and run outside Unity. Only what the headless tests need is here.

using System.Collections.Generic;
using MoonscraperChartEditor.Song;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) { return o != null; } }
    public class Component : Object { public GameObject gameObject; }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object { public void SetActive(bool active) { } }
    public class TextAsset : Object { public string text; public string name; }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1); } }
    }
    public enum KeyCode { F9, F10 }

    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogFormat(string format, params object[] args) { }
        public static void LogError(object o) { System.Console.WriteLine("ERROR: " + o); }
        public static void LogErrorFormat(string format, params object[] args) { System.Console.WriteLine("ERROR: " + string.Format(format, args)); }
        public static void LogWarning(object o) { System.Console.WriteLine("WARNING: " + o); }
        public static void Assert(bool condition, string message = null) { if (!condition) System.Console.WriteLine("ASSERT: " + message); }
    }

    public static class Mathf
    {
        public static float Round(float f) { return (float)System.Math.Round(f); }
        public static float Ceil(float f) { return (float)System.Math.Ceiling(f); }
        public static float Floor(float f) { return (float)System.Math.Floor(f); }
        public static int RoundToInt(float f) { return (int)System.Math.Round(f); }
        public static int FloorToInt(float f) { return (int)System.Math.Floor(f); }
        public static int CeilToInt(float f) { return (int)System.Math.Ceiling(f); }
        public static float Pow(float a, float b) { return (float)System.Math.Pow(a, b); }
        public static float Log(float a, float b) { return (float)System.Math.Log(a, b); }
        public static float Abs(float f) { return System.Math.Abs(f); }
        public static float Max(float a, float b) { return System.Math.Max(a, b); }
        public static float Min(float a, float b) { return System.Math.Min(a, b); }
        public static int Max(int a, int b) { return System.Math.Max(a, b); }
        public static int Min(int a, int b) { return System.Math.Min(a, b); }
        public static float Clamp(float v, float min, float max) { return System.Math.Max(min, System.Math.Min(max, v)); }
        public static int Clamp(int v, int min, int max) { return System.Math.Max(min, System.Math.Min(max, v)); }
    }

    public static class Input
    {
        public static bool anyKey;  // Set by tests to simulate holding a key or mouse button
        public static Vector3 mousePosition;
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetMouseButton(int button) { return false; }
    }

    public static class Application { public static string persistentDataPath = System.IO.Path.GetTempPath(); }

    // Just enough of Unity's immediate-mode GUI for the Duet panel to compile
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } }
    public struct Matrix4x4 { public static Matrix4x4 Scale(Vector3 v) { return new Matrix4x4(); } }
    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public bool Contains(Vector2 point) { return point.x >= x && point.x < x + width && point.y >= y && point.y < y + height; }
    }
    public static class Screen { public static int width = 1920, height = 1080; }
    public enum EventType { MouseDown }
    public class Event { public static Event current = new Event(); public EventType type; public Vector2 mousePosition; }
    public class GUILayoutOption { }
    public static class GUIUtility { public static int keyboardControl; public static string systemCopyBuffer; }
    public class GUIStyle { }
    public class GUISkin { public GUIStyle button = new GUIStyle(); }
    public static class GUI { public delegate void WindowFunction(int id); public static GUISkin skin = new GUISkin(); public static Matrix4x4 matrix; public static Color backgroundColor; public static void DragWindow() { } }
    public static class GUILayout
    {
        public static Rect Window(int id, Rect rect, GUI.WindowFunction func, string text) { return rect; }
        public static void Label(string text) { }
        public static string TextField(string text, int maxLength) { return text; }
        public static bool Button(string text, params GUILayoutOption[] options) { return false; }
        public static bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options) { return value; }
        public static bool Toggle(bool value, string text, params GUILayoutOption[] options) { return value; }
        public static void Space(float pixels) { }
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static GUILayoutOption Width(float width) { return new GUILayoutOption(); }
    }
    public static class PlayerPrefs
    {
        public static string GetString(string key, string defaultValue) { return defaultValue; }
        public static int GetInt(string key, int defaultValue) { return defaultValue; }
        public static void SetString(string key, string value) { }
        public static void SetInt(string key, int value) { }
        public static bool HasKey(string key) { return false; }
    }
    public static class Time { public static float deltaTime; }
}

public class LaneInfo : UnityEngine.MonoBehaviour { public int laneCount; public int laneMask; }
public class SongObjectController : UnityEngine.MonoBehaviour { public void SetDirty() { } }
public class NoteController : SongObjectController { }

public class SustainGap { public static implicit operator int(SustainGap gap) { return 0; } }

public class GameSettings
{
    public bool extendedSustainsEnabled, openChordsEnabled, tapOpensEnabled, sustainGapEnabled, sustainGapIsTimeBased;
    public int sustainGapTimeMs;
    public SustainGap sustainGap = new SustainGap();
}

public static class Globals
{
    public const string LINE_ENDING = "\r\n";
    public const string TABSPACE = "  ";
    public enum ViewMode { Chart, Song }

    public static bool drumMode;
    public static bool ghLiveMode;
    public static GameSettings gameSettings = new GameSettings();
    public static string autosaveLocation;
    public static string CONFIG_FOLDER = string.Empty;
    public static string realWorkingDirectory = string.Empty;
    public static string[] validAudioExtensions = new string[0];
}

public class SelectedObjectsManager
{
    public IList<SongObject> currentSelectedObjects = new List<SongObject>();
    public SongObject currentSelectedObject;
    public void TryFindAndSelectSongObjects(IList<SongObject> songObjects) { }
}

public class SongObjectPoolManager { public void SetAllPoolsDirty() { } }
public class LoadingTasksManager { public bool isRunningTask; }
public class Services { public LoadingTasksManager loadingTasksManager = new LoadingTasksManager(); }
public static class FileExplorer { public static bool OpenFolderPanel(out string path) { path = null; return false; } }
public class ErrorManager { public void QueueErrorMessage(string message) { System.Console.WriteLine("MESSAGE: " + message); } }
public class EditorObjectToolManager { public enum ToolID { Note } public ToolID currentToolId; }

public class ChartEditor : UnityEngine.MonoBehaviour
{
    public static ChartEditor Instance;
    public static bool isDirty;

    public Song currentSong;
    public Chart currentChart;
    public SelectedObjectsManager selectedObjectsManager = new SelectedObjectsManager();
    public SongObjectPoolManager songObjectPoolManager = new SongObjectPoolManager();
    public ErrorManager errorManager = new ErrorManager();
    public EditorObjectToolManager toolManager = new EditorObjectToolManager();

    public WindowHandleManager windowHandleManager = new WindowHandleManager();
    public Services services = new Services();
    public SongEditGestureTracker songEditGestureTracker;
    public bool isLoadingCollaborationSong;

    public bool EditCheck() { return true; }
    public void LoadCollaborationSong(Song song, bool reloadAudio, bool keepView) { currentSong = song; }
    public void UnloadCollaborationSong() { currentSong = new Song(); }

    public void FillUndoRedoSnapInfo(uint tick, Globals.ViewMode viewMode) { }
    public static float TimeToWorldYPosition(float time) { return time; }
    public static float WorldYPositionToTime(float position) { return position; }
}

public static class Snapable { public static uint TickToSnappedTick(uint tick, int step, Song song) { return tick; } }
public static class Utility { public static bool validateExtension(string path, string[] extensions) { return true; } }

public class NativeWindow { }
public class WindowHandleManager { public NativeWindow nativeWindow = new NativeWindow(); }

public static class NativeMessageBox
{
    public enum Type { OK }
    public enum Result { OK = 1 }
    public static Result Show(string text, string caption, Type type, NativeWindow childWindow) { System.Console.WriteLine(caption + ": " + text); return Result.OK; }
}
