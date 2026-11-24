using UnityEngine;

public partial class PlayerCombat
{
    private static PlayerJumpMotion jumpMotion;

    private static PlayerJumpMotion JumpMotion => jumpMotion ? jumpMotion
        : jumpMotion = Resources.Load<PlayerJumpMotion>("PlayerJumpMotion");

    public Vector3 JumpPoseOffset(float elapsed)
    {
        float duration = GetSkillDuration(PlayerSkill.JumpAttack);
        return duration > 0f && JumpMotion ? JumpMotion.Offset(elapsed / duration) : Vector3.zero;
    }

#if UNITY_EDITOR
    [ContextMenu("Bake Jump XZ From Clip")]
    private void BakeJumpPlanarMotion()
    {
        var animator = System.Array.Find(GetComponentsInChildren<Animator>(true),
            candidate => candidate.avatar && candidate.avatar.isHuman);
        if (!jumpAttackClip || !animator)
        {
            Debug.LogWarning("Assign the jump clip and a humanoid Animator to bake jump movement.", this);
            return;
        }

        var x = UnityEditor.AnimationUtility.GetEditorCurve(jumpAttackClip,
            UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.x"));
        var z = UnityEditor.AnimationUtility.GetEditorCurve(jumpAttackClip,
            UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.z"));
        if (x == null || z == null || jumpAttackClip.length <= 0f)
        {
            Debug.LogWarning("The jump clip needs humanoid RootT.x and RootT.z curves.", this);
            return;
        }

        float startX = x.Evaluate(0f), startZ = z.Evaluate(0f);
        int frames = Mathf.Max(1, Mathf.RoundToInt(jumpAttackClip.length * jumpAttackClip.frameRate));
        var samples = new Vector2[frames + 1];
        // Humanoid translation keys are expressed in units of the retargeted avatar's human scale.
        float scale = animator.humanScale;
        if (scale <= 0f)
        {
            Debug.LogWarning("Open the player in Prefab Mode with a valid humanoid avatar to bake jump movement.", this);
            return;
        }
        for (int i = 1; i <= frames; i++)
        {
            float time = jumpAttackClip.length * i / frames;
            Vector3 animated = new Vector3(x.Evaluate(time) - startX, 0f, z.Evaluate(time) - startZ) * scale;
            Vector3 local = transform.InverseTransformVector(animator.transform.TransformVector(animated));
            samples[i] = new Vector2(local.x, local.z);
        }

        if (!JumpMotion) return;
        UnityEditor.Undo.RecordObject(JumpMotion, "Bake jump movement");
        JumpMotion.SetSamples(samples);
        UnityEditor.EditorUtility.SetDirty(JumpMotion);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(JumpMotion);
    }
#endif
}
