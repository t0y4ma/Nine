using System.Collections.Generic;
using System.Diagnostics;

// Game-event log shown in the "Nine/オンライン デバッグ" window.
// Calls are removed from release builds (only compiled into the Editor and Development Builds).
// It holds server-side secrets (who played which card before reveal), so it must never be sent to clients.
public static class NineDebugLog
{
    public const int Max = 300;
    public static readonly List<string> Lines = new();
    public static int Version { get; private set; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Add(string roomId, string message)
    {
        float t = UnityEngine.Time.unscaledTime;
        Lines.Add(t.ToString("0.0").PadLeft(7) + "  [" + (string.IsNullOrEmpty(roomId) ? "-" : roomId) + "] " + message);
        if (Lines.Count > Max) Lines.RemoveRange(0, Lines.Count - Max);
        Version++;
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Clear()
    {
        Lines.Clear();
        Version++;
    }
}
