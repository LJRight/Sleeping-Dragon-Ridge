using UnityEngine;
using UnityEngine.Rendering.Universal;

// Client-only presentation. All dimensions, the fixed facing and progress come from combat.
[DisallowMultipleComponent]
public class AttackRangeDecal : MonoBehaviour
{
    [SerializeField] private Material decalTemplate;
    private static readonly int OriginRange = Shader.PropertyToID("_OriginRange");
    private static readonly int ForwardAngle = Shader.PropertyToID("_ForwardAngle");
    private static readonly int Tint = Shader.PropertyToID("_Tint");
    private static readonly int BossCue = Shader.PropertyToID("_BossCue");
    private static readonly Vector4 PlayerTint = new(.03f, .3f, 1f, 1f);
    private static readonly Vector4 EnemyTint = new(1f, .035f, .02f, 1f);

    private PlayerCombat player;
    private BossCombat boss;
    private DragonMonster dragon;
    private NetworkHealth health;
    private DecalProjector projector;
    private Material material;

    private void Awake()
    {
        player = GetComponent<PlayerCombat>();
        boss = GetComponent<BossCombat>();
        dragon = GetComponent<DragonMonster>();
        health = GetComponent<NetworkHealth>();
    }

    private void LateUpdate()
    {
        if (health && health.IsActive && (health.IsLocalTest || health.IsClient) && !health.IsDead)
        {
            if (boss && boss.TryGetTelegraph(out var cue))
            {
                Draw(cue.origin, cue.forward, cue.range, cue.angle, EnemyTint,
                    new Vector4(cue.rectangle ? 1f : 2f, Mathf.Clamp01(cue.fill), cue.width, cue.danger ? 1f : 0f));
                return;
            }
            if (player && player.IsSpinningSlash)
            {
                Draw(transform.position, transform.forward, player.AttackRadius, player.AttackArc, PlayerTint, Vector4.zero);
                return;
            }
            if (player && player.TryGetSkillArea(out var origin, out var forward, out float range, out float width)
                && player.EstimatedServerTime - player.CurrentCombo.StartedAt <= player.GetSkillHitTime(player.CurrentSkill))
            {
                bool rectangle = player.CurrentSkill == PlayerSkill.Uppercut;
                Draw(origin, forward, range, 360f, PlayerTint, new Vector4(rectangle ? 1f : 2f, 0f, width, 0f));
                return;
            }
            if (!boss && dragon && health.IsArenaBoss && dragon.CurrentState == DragonMonster.State.BasicAttack)
            {
                Draw(transform.position, transform.forward, dragon.AttackRadius, dragon.AttackArc, EnemyTint, Vector4.zero);
                return;
            }
        }
        Hide();
    }

    private void Draw(Vector3 origin, Vector3 forward, float range, float angle, Vector4 tint, Vector4 bossCue)
    {
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (!decalTemplate || range <= 0 || forward.sqrMagnitude < .5f)
        {
            Hide();
            return;
        }

        if (!projector)
        {
            material = new Material(decalTemplate);
            var go = new GameObject("Attack range (client decal)");
            projector = go.AddComponent<DecalProjector>();
            projector.material = material;
            projector.renderingLayerMask = 1u << 7;
            projector.drawDistance = 60f;
            projector.pivot = Vector3.zero;
        }

        bool rectangle = bossCue.x == 1f;
        Vector3 center = rectangle ? origin + forward * (range * .5f) : origin;
        float span = 2f * range + .1f;
        var size = rectangle ? new Vector3(bossCue.z + .1f, range + .1f, span) : Vector3.one * span;
        projector.transform.SetPositionAndRotation(center, Quaternion.LookRotation(Vector3.down, forward));
        if (projector.size != size) projector.size = size;
        material.SetVector(OriginRange, new Vector4(origin.x, origin.y, origin.z, range));
        material.SetVector(ForwardAngle, new Vector4(forward.x, 0f, forward.z, angle));
        material.SetVector(Tint, tint);
        material.SetVector(BossCue, bossCue);
        projector.enabled = true;
    }

    private void Hide()
    {
        if (projector) projector.enabled = false;
    }

    private void OnDisable() => Hide();

    private void OnDestroy()
    {
        if (projector) Destroy(projector.gameObject);
        if (material) Destroy(material);
    }
}
