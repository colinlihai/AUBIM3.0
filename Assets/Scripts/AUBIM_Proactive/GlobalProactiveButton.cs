using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

[RequireComponent(typeof(Button))]
public class GlobalProactiveButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static GlobalProactiveButton Instance;

    private Button _btn;
    private Image _btnImage;
    private Coroutine _breathCoroutine;

    [Header("状态监控")]
    public bool isBreathing = false;
    private InterventionType _pendingType;
    private bool _isHovering = false;

    // 视觉配置
    private Color normalColor = new Color(1f, 1f, 1f, 0.2f); // 幽灵态（透明度0.2）
    private Color hoverColor = new Color(0.3f, 0.3f, 0.3f, 1f);    // 鼠标悬停态（完全不透明的纯白）
    private Color glowColor = new Color(1f, 0.8f, 0.2f, 1f); // 闪烁时的耀眼金色

    void Awake()
    {
        Instance = this;
        _btn = GetComponent<Button>();
        _btnImage = GetComponent<Image>();
        _btnImage.color = normalColor;

        _btn.onClick.AddListener(OnButtonClicked);
    }

    // ==========================================
    // 鼠标悬停事件侦测
    // ==========================================
    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovering = true;
        // 如果不在闪烁状态，立刻恢复不透明
        if (!isBreathing)
        {
            _btnImage.color = hoverColor;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovering = false;
        // 如果不在闪烁状态，恢复半透明幽灵态
        if (!isBreathing)
        {
            _btnImage.color = normalColor;
        }
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

        Debug.Log($"<color=yellow>[AI 介入]</color> 全局反思按钮开始闪烁，限时 10 秒等待采纳...");
    }

    // 供 InterventionTracker 打断时呼叫
    public void StopBreathingEarly()
    {
        if (isBreathing)
        {
            isBreathing = false;
            if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);

            // 熄灭时，如果鼠标还停在上面就保持白实心，否则变回半透明
            _btnImage.color = _isHovering ? hoverColor : normalColor;
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
            _btnImage.color = _isHovering ? hoverColor : normalColor;

            // 告诉 ML Tracker：用户显性采纳了全局建议！(Score: +1.0)
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnButtonClicked(_pendingType.ToString());
            }

            // 呼叫生成系统 (isManual 传 false)
            ProactiveInterventionSystem.Instance.GenerateGlobalNodeIntervention(_pendingType, false);
        }
        else
        {
            // 场景 B：【主动索求】按钮没闪烁，用户主动点击
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.AbortLocalBreathing();
            }

            // 呼叫生成系统 (强制使用苏格拉底反问，isManual 传 true)
            ProactiveInterventionSystem.Instance.GenerateGlobalNodeIntervention(InterventionType.Socratic, true);
        }
    }

    // ==========================================
    // 核心修改：带 10 秒超时检测的呼吸协程
    // ==========================================
    private IEnumerator BreathRoutine()
    {
        float timer = 0f;
        float elapsed = 0f;

        // 【新增 10 秒存活判定】：只有在 10 秒内才保持呼吸
        while (isBreathing && elapsed < 10f)
        {
            timer += Time.deltaTime * 2f;
            elapsed += Time.deltaTime;

            float lerp = (Mathf.Sin(timer) + 1f) / 2f;

            // 细节优化：如果呼吸时鼠标悬停在上面，底色用纯白而不是半透明，混合金色效果更好
            Color baseColor = _isHovering ? hoverColor : normalColor;
            _btnImage.color = Color.Lerp(baseColor, glowColor, lerp);

            yield return null;
        }

        // 如果 10 秒走完，且 isBreathing 依然为 true（即没有被点击，也没有被用户打断）
        if (isBreathing)
        {
            isBreathing = false;
            _btnImage.color = _isHovering ? hoverColor : normalColor;

            // 【向 ML 模型汇报】：用户彻底无视了这次长达 10 秒的呼唤
            if (InterventionTracker.Instance != null)
            {
                // 这将触发扣除 0.2 分，并增加容忍度，同时解除 Tracker 的静默锁
                InterventionTracker.Instance.OnInterventionIgnored(_pendingType.ToString());
            }

            Debug.Log($"<color=yellow>[AI 介入]</color> 10 秒已过，用户无视了全局建议，按钮自动熄灭 (-0.2分)。");
        }
    }
}