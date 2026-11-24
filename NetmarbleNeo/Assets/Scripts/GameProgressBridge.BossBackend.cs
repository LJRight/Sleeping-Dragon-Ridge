using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public sealed partial class GameProgressBridge
{
    [Serializable] private sealed class BossStartBody { public string attemptId; public string[] members; }
    [Serializable] private sealed class BossClearBody { public string attemptId; }
    [Serializable] private sealed class BossReceipt { public string attemptId; public string[] eligible; public bool cleared; }

    internal static string BossAccountId(PlayerCombat player) => For(player)?.id;

    internal static Coroutine StartBossAttempt(string attemptId, string[] members, Action<bool, string> done)
    {
        var json = JsonUtility.ToJson(new BossStartBody { attemptId = attemptId, members = members });
        return SendBossCommand(attemptId, json, false, done);
    }

    internal static Coroutine ClearBossAttempt(string attemptId, Action<bool, string> done)
    {
        var json = JsonUtility.ToJson(new BossClearBody { attemptId = attemptId });
        return SendBossCommand(attemptId, json, true, done);
    }

    private static Coroutine SendBossCommand(string id, string json, bool clear, Action<bool, string> done)
    {
        if (!instance || string.IsNullOrEmpty(instance.internalKey))
        {
            done(false, "게임 서버의 진행 저장 설정을 확인하세요.");
            return null;
        }
        return instance.StartCoroutine(instance.PostBoss(id, json, clear, done));
    }

    private IEnumerator PostBoss(string id, string json, bool clear, Action<bool, string> done)
    {
        string url = authentication.BaseUrl + (clear ? "/internal/boss/clear" : "/internal/boss/start");
        byte[] payload = Encoding.UTF8.GetBytes(json);
        int delay = 1;
        while (true)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-Game-Server-Key", internalKey);
                request.timeout = 8;
                yield return request.SendWebRequest();
                string response = request.downloadHandler.text;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    BossReceipt receipt = null;
                    try { receipt = JsonUtility.FromJson<BossReceipt>(response); }
                    catch (ArgumentException) { }
                    if (receipt?.attemptId == id && (!clear || receipt.cleared))
                    {
                        if (clear && receipt.eligible != null)
                            foreach (string member in receipt.eligible)
                                if (accounts.TryGetValue(member, out var account) && account.owner)
                                {
                                    account.reload = true;
                                    StartWork(account);
                                }
                        done(true, "");
                        yield break;
                    }
                }
                if (request.responseCode >= 400 && request.responseCode < 500 &&
                    request.responseCode != 408 && request.responseCode != 429)
                {
                    string reason = response.Contains("RIDGE_SWORD_EQUIPPED") ? "잠든 용의 검을 해제한 후 도전하세요."
                        : response.Contains("BOSS_QUEST_LOCKED") ? "모든 참가자가 보스 관문을 개방해야 합니다."
                        : "보스 도전 정보를 확인할 수 없습니다. 다시 시도하세요.";
                    done(false, reason);
                    yield break;
                }
            }
            // The service may have committed before the response was lost. Retry the same attempt.
            yield return new WaitForSecondsRealtime(delay);
            delay = Mathf.Min(delay * 2, 12);
        }
    }
}
