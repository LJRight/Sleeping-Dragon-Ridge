using UnityEngine;
using UnityEngine.SceneManagement;

// Explicitly scoped to this scene; never starts a NetworkManager or writes network state.
[DefaultExecutionOrder(-10000)]
public sealed class LocalCombatTest : MonoBehaviour
{
    public const string SceneName = "Player Controll Test";
    public static bool InScene(Scene scene) => scene.IsValid() && scene.name == SceneName;
    public static bool IsLocal(Component component) => component && InScene(component.gameObject.scene);
}
