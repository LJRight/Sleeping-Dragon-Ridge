using System;
using System.Globalization;
using System.Net;
using UnityEngine;

// Read on Unity's main thread, for example from a HUD refresh timer once a second.
public static class GameHudData
{
    public static int? Level => LoginSession.Instance?.Level;
    public static double? Experience => LoginSession.Instance?.Experience;
    public static string ExperienceText => Experience?.ToString("F2", CultureInfo.InvariantCulture) ?? "—";
    public static string LocalTime => DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static int? BatteryPercent
    {
        get
        {
            float charge = SystemInfo.batteryLevel;
            return charge < 0f ? (int?)null : Mathf.RoundToInt(Mathf.Clamp01(charge) * 100f);
        }
    }

    public static string NetworkLabel
    {
        get
        {
            switch (Application.internetReachability)
            {
                case NetworkReachability.ReachableViaLocalAreaNetwork:
                    return IsLocalEndpoint(LoginSession.Instance?.GameServerAddress) ? "LAN" : "Wi-Fi";
                case NetworkReachability.ReachableViaCarrierDataNetwork:
                    return CellularLabel();
                default:
                    return "Offline";
            }
        }
    }

    private static bool IsLocalEndpoint(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal) return true;
        byte[] b = ip.GetAddressBytes();
        if (b.Length == 16) return (b[0] & 0xfe) == 0xfc;
        return b[0] == 10 || (b[0] == 192 && b[1] == 168)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 169 && b[1] == 254);
    }

#if UNITY_IOS && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int HudCellularGeneration();
#endif

    private static string CellularLabel()
    {
        int generation = 0;
#if UNITY_IOS && !UNITY_EDITOR
        generation = HudCellularGeneration();
#elif UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var manager = activity.Call<AndroidJavaObject>("getSystemService", "connectivity");
            // Best effort without requesting a runtime phone-state permission.
            using var network = manager.Call<AndroidJavaObject>("getActiveNetworkInfo");
            if (network != null && network.Call<int>("getType") == 0)
            {
                int subtype = network.Call<int>("getSubtype");
                if (subtype == 20) generation = 5; // NETWORK_TYPE_NR
                else if (subtype == 13) generation = 4; // NETWORK_TYPE_LTE
            }
        }
        catch (AndroidJavaException) { return "Data"; }
#endif
        return generation == 5 ? "5G" : generation == 4 ? "LTE" : "Data";
    }
}
