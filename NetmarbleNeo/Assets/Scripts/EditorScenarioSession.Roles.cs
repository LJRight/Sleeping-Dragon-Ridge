#if UNITY_EDITOR
using System;
using Unity.Multiplayer.PlayMode;

internal static partial class EditorScenarioSession
{
    internal static bool IsScenarioServer => CurrentPlayer.IsMainEditor && HasTag("EditorGameServer");
    internal static bool IsScenarioClient => HasTag("EditorGameClient");

    private static bool HasTag(string expected)
    {
        foreach (var tag in CurrentPlayer.Tags)
            if (string.Equals(tag, expected, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
#endif
