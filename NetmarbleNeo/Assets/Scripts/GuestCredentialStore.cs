using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

// Save the pending seed before creating an account. A lost create response can be replayed.
internal static class GuestCredentialStore
{
    internal static string MintSeed()
    {
        var bytes = new byte[32];
        using var random = RandomNumberGenerator.Create();
        random.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int NeoGuestRead(StringBuilder buffer, int capacity);
    [DllImport("__Internal")] private static extern int NeoGuestWrite(string value);
#endif

    internal static string Read()
    {
#if UNITY_IOS && !UNITY_EDITOR
        var buffer = new StringBuilder(512);
        int status = NeoGuestRead(buffer, buffer.Capacity);
        if (status < 0) throw new IOException("Keychain unavailable");
        string value = status == 0 ? null : buffer.ToString();
#elif UNITY_ANDROID && !UNITY_EDITOR
        using var store = new AndroidJavaClass("com.neoridge.auth.GuestStore");
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var context = player.GetStatic<AndroidJavaObject>("currentActivity");
        string value = store.CallStatic<string>("read", context);
#else
        string value;
        try { value = File.ReadAllText(DesktopPath()); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
#endif
        if (value == null) return null;
        if (value.Length < 34 || value.Length > 450 ||
            !(value.StartsWith("P:", StringComparison.Ordinal) || value.StartsWith("R:", StringComparison.Ordinal)))
            throw new InvalidDataException("Guest credential is unreadable");
        return value;
    }

    internal static string LoadOrCreate()
    {
        string existing = Read();
        if (existing != null) return existing;
        string pending = "P:" + MintSeed();
#if !(UNITY_IOS || UNITY_ANDROID) || UNITY_EDITOR
        // Independent desktop processes sharing one profile must converge on the same first seed.
        try { WriteDesktop(pending, false); }
        catch (IOException)
        {
            existing = Read();
            if (existing == null) throw;
            return existing;
        }
#else
        Write(pending);
#endif
        return pending;
    }

    internal static string ReplaceRejected(string rejected)
    {
#if !(UNITY_IOS || UNITY_ANDROID) || UNITY_EDITOR
        // Two editor processes can share a profile and both see the same rejection.
        string path = DesktopPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var recoveryLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
#endif
        string current = LoadOrCreate();
        if (current != rejected) return current;
        string seed = "P:" + MintSeed();
        Write(seed);
        return seed;
    }

    internal static void Write(string value)
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (NeoGuestWrite(value) != 0) throw new IOException("Keychain write failed");
#elif UNITY_ANDROID && !UNITY_EDITOR
        using var store = new AndroidJavaClass("com.neoridge.auth.GuestStore");
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var context = player.GetStatic<AndroidJavaObject>("currentActivity");
        store.CallStatic("write", context, value);
#else
        WriteDesktop(value, true);
#endif
    }

#if !(UNITY_IOS || UNITY_ANDROID) || UNITY_EDITOR
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint="chmod")]
    private static extern int Chmod(string path, uint mode);
#endif
    private static void WriteDesktop(string value, bool replace)
    {
        var path = DesktopPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
                if (Chmod(temp, 384) != 0) throw new IOException("Cannot restrict guest file permissions");
#endif
                var data = Encoding.UTF8.GetBytes(value);
                file.Write(data, 0, data.Length);
                file.Flush(true);
            }
            if (replace && File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static string DesktopPath()
    {
        string profile = Environment.GetEnvironmentVariable("NEO_GUEST_PROFILE");
#if UNITY_EDITOR
        // Multiplayer Play Mode virtual projects have distinct data paths.
        if (string.IsNullOrEmpty(profile)) profile = Application.dataPath;
#endif
        using var hash = SHA256.Create();
        var key = hash.ComputeHash(Encoding.UTF8.GetBytes(profile ?? "default"));
        var file = Path.Combine("Guest", BitConverter.ToString(key, 0, 12) + ".dat");
        var current = Path.Combine(Application.persistentDataPath, file);
        // Desktop data paths include the product name. Continue an existing pre-rename profile.
        var companyFolder = Path.GetDirectoryName(Application.persistentDataPath);
        if (companyFolder == null || File.Exists(current)) return current;
        var previous = Path.Combine(companyFolder, "NetmarbleNeo", file);
        return File.Exists(previous) ? previous : current;
    }
#endif
}
