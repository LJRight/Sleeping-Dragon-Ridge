using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

internal sealed class MonsterRewardEditor : IDisposable
{
    private static readonly string[] SummaryKeys = { "xp", "gold" };

    // Undo tracks the exact file contents, including unrelated rows, as a single edit.
    private sealed class UndoState : ScriptableObject
    {
        public string json;
    }

    private UndoState state;
    private JObject document;
    private string path;
    private string savedJson;
    private string error;

    private static bool IsBoss(string id) => id == "BOSS" || id == "ridge_boss";

    private JObject GetRow(string id)
    {
        if (state == null)
        {
            state = ScriptableObject.CreateInstance<UndoState>();
            state.hideFlags = HideFlags.HideAndDontSave;
            path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "localServer", "monster_rewards.json"));
            Undo.undoRedoPerformed += UndoRedo;
            Reload();
        }
        var fields = document?["field"] as JObject;
        var row = IsBoss(id) ? document?["bossReport"] as JObject : fields?[id] as JObject;
        if (row == null && error == null)
            error = $"보상표에 {id} 행이 없습니다.";
        return row;
    }

    internal void DrawSummaryCells(string monsterId, float columnWidth)
    {
        var row = GetRow(monsterId);
        foreach (string key in SummaryKeys)
        {
            if (row?[key]?.Type != JTokenType.Integer)
                GUILayout.Label("—", GUILayout.Width(columnWidth));
            else
                Number(row, key, null, 0, 100000, columnWidth);
        }
    }

    internal void DrawStatus()
    {
        if (string.IsNullOrEmpty(error))
            return;
        EditorGUILayout.HelpBox(error, MessageType.Warning);
        if (GUILayout.Button("저장된 보상 다시 읽기"))
            Reload();
    }

    internal void DrawDetails(string monsterId)
    {
        var row = GetRow(monsterId);
        if (row == null)
            return;

        if (IsBoss(monsterId))
        {
            EditorGUILayout.LabelField("보스 의뢰 보고 보상", EditorStyles.boldLabel);
            Number(row, "oreCount", "강화석 수량", 0, 9999);
            Number(row, "refinedOreCount", "정제 강화석 수량", 0, 9999);
            EditorGUILayout.LabelField("확정 장비", "전설 잠든 용의 검 ×1");
            EditorGUILayout.HelpBox("보스 처치 후 경비대장에게 보고하면 계정당 한 번 지급합니다. 경험치와 골드는 위 표에서 조절합니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("장비 드롭", EditorStyles.boldLabel);
        const string common = "commonGearChance", rare = "rareGearChance", legendary = "legendaryGearChance";
        Number(row, common, "일반 장비 확률 (%)", 0, 100 - Value(row, rare) - Value(row, legendary));
        Number(row, rare, "레어 장비 확률 (%)", 0, 100 - Value(row, common) - Value(row, legendary));
        Number(row, legendary, "전설 장비 확률 (%)", 0, 100 - Value(row, common) - Value(row, rare));
        EditorGUILayout.LabelField("장비 미획득", $"{100 - Value(row, common) - Value(row, rare) - Value(row, legendary)}%");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("강화 재료", EditorStyles.boldLabel);
        Number(row, "oreChance", "강화석 확률 (%)", 0, 100);
        Number(row, "oreCount", "강화석 수량", 0, 9999);
        Number(row, "refinedOreChance", "정제 강화석 확률 (%)", 0, 100);
        Number(row, "refinedOreCount", "정제 강화석 수량", 0, 9999);

        if (monsterId == "A2" || monsterId == "B1" || monsterId == "B1G")
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("수락 중인 수집 의뢰", EditorStyles.boldLabel);
            var drops = document["questDropChance"] as JObject;
            if (monsterId == "A2")
                Number(drops, "ember_heartstone", "잿불 심장석 확률 (%)", 1, 100);
            else
                Number(drops, "sturdy_tusk", "단단한 엄니 확률 (%)", 1, 100);
            if (monsterId != "A2")
                EditorGUILayout.LabelField("단단한 엄니 확률은 일반·황금 멧돼지가 공유합니다.", EditorStyles.wordWrappedMiniLabel);
        }
    }

    private static int Value(JObject row, string key) => (int?)row?[key] ?? 0;

    private void Number(JObject row, string key, string label, int minimum, int maximum, float width = 0)
    {
        if (row?[key]?.Type != JTokenType.Integer)
        {
            EditorGUILayout.LabelField(label ?? key, "보상 설정 누락");
            return;
        }
        int current = Value(row, key);
        int input = label == null
            ? EditorGUILayout.DelayedIntField(current, GUILayout.Width(width))
            : EditorGUILayout.DelayedIntField(label, current);
        if (input != current)
            ApplyNumber(row, key, label, input, minimum, maximum);
    }

    private void ApplyNumber(JObject row, string key, string label, int input, int minimum, int maximum)
    {
        if (input < minimum || input > maximum)
        {
            error = $"{label ?? (key == "xp" ? "경험치" : "골드")}에 {minimum}~{maximum} 사이의 값을 입력하세요.";
            return;
        }

        var previous = row[key];
        row[key] = input;
        if (TryWrite(document.ToString(Formatting.Indented) + "\n"))
        {
            Undo.RegisterCompleteObjectUndo(state, "몬스터 보상 수정");
            state.json = savedJson;
        }
        else
        {
            row[key] = previous;
        }
    }

    private void Reload()
    {
        try
        {
            string json = File.ReadAllText(path);
            var content = JObject.Parse(json);
            Undo.ClearUndo(state);
            document = content;
            savedJson = state.json = json;
            error = null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            document = null;
            error = $"보상 파일을 읽을 수 없습니다. {ex.Message}";
        }
    }

    private bool TryWrite(string json)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            if (File.ReadAllText(path) != savedJson)
            {
                error = "다른 곳에서 보상 파일을 수정했습니다. 저장된 보상을 다시 읽고 변경을 적용하세요.";
                return false;
            }
            File.Replace(temp, path, null);
            savedJson = json;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            error = $"보상 저장 실패. {ex.Message}";
            return false;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private void UndoRedo()
    {
        if (state.json == savedJson)
            return;
        if (TryWrite(state.json))
            document = JObject.Parse(state.json);
        else
            state.json = savedJson;
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    public void Dispose()
    {
        if (state == null)
            return;
        Undo.undoRedoPerformed -= UndoRedo;
        Undo.ClearUndo(state);
        UnityEngine.Object.DestroyImmediate(state);
        state = null;
        document = null;
    }
}
