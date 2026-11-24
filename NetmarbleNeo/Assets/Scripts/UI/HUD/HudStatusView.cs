using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HudStatusView : MonoBehaviour
{
    [SerializeField] private TMP_Text network;
    [SerializeField] private Slider battery;
    [SerializeField] private TMP_Text clock;
    [SerializeField] private TMP_Text experience;

    private void OnEnable() => StartCoroutine(RefreshHud());
    private void OnDisable() => StopAllCoroutines();

    private IEnumerator RefreshHud()
    {
        var interval = new WaitForSecondsRealtime(1f);

        while (true)
        {
            network.text = GameHudData.NetworkLabel;
            battery.value = GameHudData.BatteryPercent is int percent ? percent : 0;
            clock.text = GameHudData.LocalTime;
            experience.text = $"{GameHudData.ExperienceText}%";

            yield return interval;
        }
    }
}
