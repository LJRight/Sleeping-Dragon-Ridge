#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

// Apply permissions to each generated Xcode project; no manual plist edits after every export.
public sealed class IosLocalNetworkBuildSettings : IPostprocessBuildWithReport
{
    private const string LocalNetworking = "NSAllowsLocalNetworking";
    private const string HttpException = "NSExceptionAllowsInsecureHTTPLoads";
    private const string SavedSettings = "RidgeDevWifiPlistSettings";
    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;
        string path = Path.Combine(report.summary.outputPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(path);
        plist.root.SetString("NSLocalNetworkUsageDescription",
            "같은 네트워크에 있는 게임 서버에 연결하여 함께 플레이합니다.");

        UpdateLoginPermissions(plist.root);
        plist.WriteToFile(path);
    }

    private static void UpdateLoginPermissions(PlistElementDict root)
    {
        var ats = GetOrCreateDictionary(root, "NSAppTransportSecurity");
        var domains = GetOrCreateDictionary(ats, "NSExceptionDomains");

        // Appending to an older LAN export can retain its per-network ATS exception.
        if (root.values.TryGetValue(SavedSettings, out var previous))
        {
            var saved = previous.AsDict();
            RestoreFlag(ats, LocalNetworking, saved);
            string oldAddress = saved.values["Host"].AsString();
            if (domains.values.TryGetValue(oldAddress, out var oldDomain))
            {
                RestoreFlag(oldDomain.AsDict(), HttpException, saved);
                if (oldDomain.AsDict().values.Count == 0) domains.values.Remove(oldAddress);
            }
            root.values.Remove(SavedSettings);
        }
        ats.SetBoolean("NSAllowsArbitraryLoads", false);
        var loginDomain = GetOrCreateDictionary(domains, MobileServerEndpoint.Host.ToLowerInvariant());
        loginDomain.SetBoolean(HttpException, true);
    }

    private static void RestoreFlag(PlistElementDict dict, string key, PlistElementDict original)
    {
        if (original.values.TryGetValue(key, out var prior)) dict.values[key] = prior;
        else dict.values.Remove(key);
    }

    private static PlistElementDict GetOrCreateDictionary(PlistElementDict parent, string key)
    {
        return parent.values.TryGetValue(key, out var value)
            ? value.AsDict() : parent.CreateDict(key);
    }
}
#endif
