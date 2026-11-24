#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

internal static class HudRadioFramework
{
    [PostProcessBuild(110)]
    private static void LinkFramework(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;
        string file = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromFile(file);
        project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "CoreTelephony.framework", false);
        project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "Security.framework", false);
        project.WriteToFile(file);
    }
}
#endif
