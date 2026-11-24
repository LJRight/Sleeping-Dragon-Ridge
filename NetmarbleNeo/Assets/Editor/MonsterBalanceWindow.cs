using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public sealed class MonsterBalanceWindow : EditorWindow
{
    private const string AssetPath = "Assets/Resources/MonsterBalanceCatalog.asset";
    private const float NameWidth = 206f;
    private const float CellWidth = 64f;
    private static readonly string[] Columns = { "최대 체력", "방어력", "기본 피해", "추격 속도", "경험치", "골드" };
    private static readonly string[] Tabs = { "전투 상세", "드롭 · 보상" };
    private static readonly string[] Pages = { "몬스터 · 보상", "플레이어 공격" };

    [SerializeField] private string selectedId = "A1";
    [SerializeField] private int selectedTab;
    [SerializeField] private int selectedPage;
    [SerializeField] private int previewAttack = 10;
    private Vector2 tableScroll, detailScroll;
    private MonsterRewardEditor rewards;
    private SerializedObject combat;
    private SerializedObject attacks;

    [MenuItem("Tools/용이 잠든 능선/몬스터 밸런스")]
    public static void Open()
    {
        GetWindow<MonsterBalanceWindow>("전투 밸런스").selectedPage = 0;
    }

    [MenuItem("Tools/용이 잠든 능선/플레이어 공격 밸런스")]
    public static void OpenPlayerAttacks()
    {
        GetWindow<MonsterBalanceWindow>("전투 밸런스").selectedPage = 1;
    }

    private void OnEnable()
    {
        minSize = new Vector2(670, 460);
        rewards = new MonsterRewardEditor();
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        rewards?.Dispose();
        combat?.Dispose();
        combat = null;
        attacks?.Dispose();
        attacks = null;
    }

    private void OnUndoRedo()
    {
        if (combat != null && combat.targetObject) AssetDatabase.SaveAssetIfDirty(combat.targetObject);
        if (attacks != null && attacks.targetObject) AssetDatabase.SaveAssetIfDirty(attacks.targetObject);
        Repaint();
    }

    private void OnGUI()
    {
        selectedPage = GUILayout.Toolbar(selectedPage, Pages, EditorStyles.toolbarButton);
        EditorGUILayout.Space(5);
        if (selectedPage == 1) DrawPlayerAttacks();
        else DrawMonsters();
    }

    private void DrawMonsters()
    {
        if (combat == null)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MonsterBalanceCatalog>(AssetPath);
            if (!catalog)
            {
                EditorGUILayout.HelpBox("전투 데이터 파일을 열 수 없습니다. " + AssetPath, MessageType.Error);
                return;
            }
            combat = new SerializedObject(catalog);
        }

        combat.Update();
        SerializedProperty rows = combat.FindProperty("fieldMonsters");
        SerializedProperty boss = combat.FindProperty("boss");
        SerializedProperty selected = boss;
        string nextSelection = selectedId;
        using (var table = new EditorGUILayout.ScrollViewScope(tableScroll, GUILayout.ExpandHeight(false)))
        {
            tableScroll = table.scrollPosition;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("몬스터", EditorStyles.miniBoldLabel, GUILayout.Width(NameWidth));
                foreach (string title in Columns)
                    GUILayout.Label(title, EditorStyles.miniBoldLabel, GUILayout.Width(CellWidth));
            }
            for (int index = 0; index < rows.arraySize; index++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(index);
                string id = row.FindPropertyRelative("id").stringValue;
                if (DrawRow(id, row, false)) nextSelection = id;
                if (selectedId == id) selected = row;
            }
            if (DrawRow(BossArena.BossId, boss, true)) nextSelection = BossArena.BossId;
        }

        rewards.DrawStatus();
        bool selectedBoss = selectedId == BossArena.BossId;
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField(NetworkHealth.DefaultMonsterName(selectedId), EditorStyles.boldLabel);
        selectedTab = GUILayout.Toolbar(selectedTab, Tabs);
        using (var details = new EditorGUILayout.ScrollViewScope(detailScroll))
        {
            detailScroll = details.scrollPosition;
            if (selectedTab == 0)
            {
                DrawDetails(selectedBoss ? BossDetails : FieldDetails, selected);
                if (selectedId == "B1" || selectedId == "B1G")
                {
                    SerializedProperty chance = combat.FindProperty("goldenBoarChance");
                    EditorGUILayout.Space();
                    EditorGUI.BeginChangeCheck();
                    float percent = EditorGUILayout.Slider("황금 멧돼지 출현 확률 (%)", chance.floatValue * 100f, 0, 100);
                    if (EditorGUI.EndChangeCheck()) chance.floatValue = percent / 100f;
                }
            }
            else rewards.DrawDetails(selectedId);
        }
        if (combat.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(combat.targetObject);
        if (selectedId != nextSelection)
        {
            selectedId = nextSelection;
            detailScroll = Vector2.zero;
            Repaint();
        }
    }

    private bool DrawRow(string id, SerializedProperty row, bool isBoss)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            bool selected = selectedId == id;
            string title = id + "   " + NetworkHealth.DefaultMonsterName(id);
            bool clicked = GUILayout.Toggle(selected, title, EditorStyles.miniButton, GUILayout.Width(NameWidth)) && !selected;
            if (clicked) GUI.FocusControl(null);
            IntegerCell(row.FindPropertyRelative(isBoss ? "maxHealth" : "maximumHealth"), 1);
            IntegerCell(row.FindPropertyRelative(isBoss ? "initialDefense" : "defense"), 0);
            IntegerCell(row.FindPropertyRelative(isBoss ? "basicDamage" : "damage"), isBoss ? 1 : 0);
            SerializedProperty speed = row.FindPropertyRelative(isBoss ? "chaseSpeed" : "runSpeed");
            EditorGUI.BeginChangeCheck();
            float entered = EditorGUILayout.DelayedFloatField(speed.floatValue, GUILayout.Width(CellWidth));
            if (EditorGUI.EndChangeCheck()) speed.floatValue = Mathf.Max(isBoss ? .1f : 0f, entered);
            rewards.DrawSummaryCells(id, CellWidth);
            return clicked;
        }
    }

    private static void IntegerCell(SerializedProperty property, int min)
    {
        EditorGUI.BeginChangeCheck();
        int number = EditorGUILayout.DelayedIntField(property.intValue, GUILayout.Width(CellWidth));
        if (EditorGUI.EndChangeCheck()) property.intValue = Mathf.Max(min, number);
    }

    private static readonly Dictionary<string, GUIContent> FieldDetails = DetailLabels(typeof(FieldMonsterBalance));
    private static readonly Dictionary<string, GUIContent> BossDetails = DetailLabels(typeof(BossMonsterBalance));

    private static Dictionary<string, GUIContent> DetailLabels(Type type)
    {
        var labels = new Dictionary<string, GUIContent>();
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.Name is "id" or "maximumHealth" or "maxHealth" or "defense" or "initialDefense"
                or "damage" or "basicDamage" or "runSpeed" or "chaseSpeed") continue;
            string label = field.GetCustomAttribute<InspectorNameAttribute>()?.displayName;
            labels.Add(field.Name, new GUIContent(label ?? ObjectNames.NicifyVariableName(field.Name),
                field.GetCustomAttribute<TooltipAttribute>()?.tooltip));
        }
        return labels;
    }

    private static void DrawDetails(Dictionary<string, GUIContent> labels, SerializedProperty row)
    {
        using var child = row.Copy();
        if (!child.NextVisible(true)) return;
        do
        {
            if (child.depth != row.depth + 1) break;
            if (labels.TryGetValue(child.name, out GUIContent label))
                EditorGUILayout.PropertyField(child, label, true);
        } while (child.NextVisible(false));
    }

    private void DrawPlayerAttacks()
    {
        if (attacks == null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<PlayerAttackBalance>("Assets/Resources/PlayerAttackBalance.asset");
            if (!asset)
            {
                EditorGUILayout.HelpBox("PlayerAttackBalance.asset 파일을 열 수 없습니다.", MessageType.Error);
                return;
            }
            attacks = new SerializedObject(asset);
        }
        attacks.Update();
        previewAttack = Mathf.Max(1, EditorGUILayout.DelayedIntField("미리보기용 공격력", previewAttack));
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("공격", EditorStyles.miniBoldLabel, GUILayout.Width(NameWidth));
            GUILayout.Label("피해 배율 (%)", EditorStyles.miniBoldLabel, GUILayout.Width(108));
            GUILayout.Label("방어 적용 전 피해", EditorStyles.miniBoldLabel);
        }
        DrawAttack("기본 공격 1타", nameof(PlayerAttackBalance.basic1Percent), PlayerSkill.None, 1);
        DrawAttack("기본 공격 2타", nameof(PlayerAttackBalance.basic2Percent), PlayerSkill.None, 2);
        DrawAttack("기본 공격 3타", nameof(PlayerAttackBalance.basic3Percent), PlayerSkill.None, 3);
        DrawAttack("회전 베기", nameof(PlayerAttackBalance.spinningSlashPercent), PlayerSkill.SpinningSlash);
        DrawAttack("어퍼컷", nameof(PlayerAttackBalance.uppercutPercent), PlayerSkill.Uppercut);
        DrawAttack("점프 공격", nameof(PlayerAttackBalance.jumpAttackPercent), PlayerSkill.JumpAttack);
        if (attacks.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(attacks.targetObject);
    }

    private void DrawAttack(string label, string key, PlayerSkill skill, int stage = 0)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(label, GUILayout.Width(NameWidth));
            SerializedProperty percent = attacks.FindProperty(key);
            EditorGUI.BeginChangeCheck();
            float input = EditorGUILayout.DelayedFloatField(percent.floatValue, GUILayout.Width(108));
            if (EditorGUI.EndChangeCheck() && float.IsFinite(input)) percent.floatValue = Mathf.Max(0f, input);
            var asset = (PlayerAttackBalance)attacks.targetObject;
            GUILayout.Label(asset.DamageBeforeDefense(previewAttack, skill, stage).ToString());
        }
    }
}

[CustomEditor(typeof(MonsterBalanceCatalog))]
internal sealed class MonsterBalanceCatalogInspector : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("몬스터 밸런스 표 열기")) MonsterBalanceWindow.Open();
    }
}

[CustomEditor(typeof(PlayerAttackBalance))]
internal sealed class PlayerAttackBalanceInspector : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("플레이어 공격 밸런스 표 열기")) MonsterBalanceWindow.OpenPlayerAttacks();
    }
}
