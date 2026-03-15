using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(Button))]
public class GlobalProactiveButton : MonoBehaviour
{
    public static GlobalProactiveButton Instance;

    private Button _btn;
    private Image _btnImage;
    private Coroutine _breathCoroutine;

    [Header("状态监控")]
    public bool isBreathing = false;
    private InterventionType _pendingType;

    // 视觉配置：默认是幽灵态（透明度0.4），闪烁时为高亮金色
    private Color normalColor = new Color(1f, 1f, 1f, 0.4f);
    private Color glowColor = new Color(1f, 0.8f, 0.2f, 1f);

    void Awake()
    {
        Instance = this;
        _btn = GetComponent<Button>();
        _btnImage = GetComponent<Image>();
        _btnImage.color = normalColor;

        _btn.onClick.AddListener(OnButtonClicked);
    }

    // ==========================================
    // 供 InterventionTracker (发呆计时器) 呼叫
    // ==========================================
    public void StartGlobalBreathing(InterventionType type)
    {
        if (isBreathing) return;

        isBreathing = true;
        _pendingType = type;
        if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);
        _breathCoroutine = StartCoroutine(BreathRoutine());

        Debug.Log($"<color=yellow>[AI 介入]</color> 全局反思按钮开始闪烁，等待用户采纳...");
    }

    // 供 InterventionTracker 打断时呼叫
    public void StopBreathingEarly()
    {
        if (isBreathing)
        {
            isBreathing = false;
            if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);
            _btnImage.color = normalColor;
        }
    }

    // ==========================================
    // 核心交互：用户点击了按钮
    // ==========================================
    private void OnButtonClicked()
    {
        if (ProactiveInterventionSystem.Instance == null) return;

        if (isBreathing)
        {
            // 场景 A：【发呆采纳】按钮正在闪烁，用户点击了它
            isBreathing = false;
            if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);
            _btnImage.color = normalColor;

            // 告诉 ML Tracker：用户显性采纳了全局建议！(Score: +1.0)
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnButtonClicked(_pendingType.ToString());
            }

            // 呼叫生成系统 (isManual 传 false，触发 proactive_global 埋点并开启45秒观察期)
            ProactiveInterventionSystem.Instance.GenerateGlobalNodeIntervention(_pendingType, false);
        }
        else
        {
            // 场景 B：【主动索求】按钮没闪烁，用户主动点击
            // 打断可能正在计时的发呆 Tracker
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.AbortLocalBreathing();
            }

            // 呼叫生成系统 (强制给一个苏格拉底反问，isManual 传 true)
            ProactiveInterventionSystem.Instance.GenerateGlobalNodeIntervention(InterventionType.Socratic, true);
        }
    }

    private IEnumerator BreathRoutine()
    {
        float timer = 0f;
        while (isBreathing)
        {
            timer += Time.deltaTime * 2f;
            // 正弦波计算呼吸效果 (在正常色和金色之间平滑过渡)
            float lerp = (Mathf.Sin(timer) + 1f) / 2f;
            _btnImage.color = Color.Lerp(normalColor, glowColor, lerp);
            yield return null;
        }
    }
}