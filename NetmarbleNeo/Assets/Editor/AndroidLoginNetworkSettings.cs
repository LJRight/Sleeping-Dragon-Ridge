#if UNITY_ANDROID
using System.IO;
using System.Xml.Linq;
using UnityEditor.Android;

public sealed class AndroidLoginNetworkSettings : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 1000;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        string main = Path.Combine(unityLibraryPath, "src", "main");
        string xmlDirectory = Path.Combine(main, "res", "xml");
        Directory.CreateDirectory(xmlDirectory);
        const string resource = "ridge_login_network_security";
        var security = new XDocument(new XElement("network-security-config",
            new XElement("base-config", new XAttribute("cleartextTrafficPermitted", "false")),
            new XElement("domain-config", new XAttribute("cleartextTrafficPermitted", "true"),
                new XElement("domain", MobileServerEndpoint.Host.ToLowerInvariant()))));
        string policyPath = Path.Combine(xmlDirectory, resource + ".xml");
        if (!File.Exists(policyPath) || !XNode.DeepEquals(XDocument.Load(policyPath), security))
            security.Save(policyPath);

        string manifestPath = Path.Combine(main, "AndroidManifest.xml");
        var manifest = XDocument.Load(manifestPath, LoadOptions.PreserveWhitespace);
        XNamespace android = "http://schemas.android.com/apk/res/android";
        var application = manifest.Root.Element("application");
        var attribute = android + "networkSecurityConfig";
        const string value = "@xml/" + resource;
        if ((string)application.Attribute(attribute) == value) return;
        application.SetAttributeValue(attribute, value);
        manifest.Save(manifestPath, SaveOptions.DisableFormatting);
    }
}
#endif
