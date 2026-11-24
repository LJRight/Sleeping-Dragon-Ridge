using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

// Field and boss map roots are translated at runtime. Static batching bakes mesh vertices
// into scene-space coordinates, which would leave rendered scenery behind its colliders.
public sealed class WorldMapBuildSettings : IProcessSceneWithReport
{
    public int callbackOrder => -1000;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (scene.name != SceneWorld.Field && scene.name != SceneWorld.Boss) return;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var item in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var flags = GameObjectUtility.GetStaticEditorFlags(item.gameObject);
            if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                GameObjectUtility.SetStaticEditorFlags(item.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
        }
    }
}
