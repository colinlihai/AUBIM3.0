using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems; // 必须引入以处理鼠标悬停
using System.Collections;

[RequireComponent(typeof(CanvasGroup))]
public class ProactiveButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("按钮身份 (下拉选择)")]
    // 【核心修改】：把 string 变成了 Enum，现在可以在 Unity 面板下拉选择了！
    public InterventionType interventionType = InterventionType.None;

    [Header("UI 引用")]
    public Button myButton;
    private CanvasGroup _canvasGroup;

    // 【修改点】：为了改变颜色，我们需要获取按钮背景的 Image 组件
    private Image _buttonImage;
    private Color _originalColor;

    [Header("视觉特效")]
    [Tooltip("被 AI 唤醒时的呼吸颜色 (金色)")]
    public Color goldenGlowColor = new Color(1f, 0.8f, 0.2f, 1f); // 温暖的灵感金

    [Header("状态标记")]
    public bool isBreathing = false; // 是否正在被 AI 选中呼吸
    private Coroutine _breathCoroutine;

    void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        if (myButton == null) myButton = GetComponent<Button>();

        // 获取图片组件并记录它原本的颜色（通常是白色或灰色）
        _buttonImage = GetComponent<Image>();
        if (_buttonImage != null)
        {
            _originalColor = _buttonImage.color;
        }

        myButton.onClick.RemoveAllListeners();
        myButton.onClick.AddListener(OnButtonClicked);

        // 初始状态：幽灵模式 (半透明)
        SetGhostMode();
    }

    void OnDisable()
    {
        // 当节点被取消选中，或者画布UI被隐藏时，Unity 会强行终止在此物体上运行的协程
        // 此时必须手动重置状态，防止下一次出现时卡在“高亮呼吸”和 isBreathing=true 的死锁中
        isBreathing = false;
        SetGhostMode();
    }

    // ==========================================
    // 鼠标悬停逻辑 (平时工具属性)
    // ==========================================
    public void OnPointerEnter(PointerEventData eventData)
    {
        // 如果鼠标移入，且它没在呼吸，就让它实体化（方便用户当作普通工具点击）
        if (!isBreathing)
        {
            _canvasGroup.alpha = 1.0f;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 鼠标移出时，如果没在呼吸，退回幽灵模式
        if (!isBreathing)
        {
            SetGhostMode();
        }
    }

    private void SetGhostMode()
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0.3f;
        }

        if (_buttonImage != null)
        {
            _buttonImage.color = _originalColor;
        }
    }

    // ==========================================
    // AI 介入逻辑 (呼吸发光)
    // ==========================================
    public void StartAIBreathing(float duration = 10f)
    {
        if (isBreathing) return;

        isBreathing = true;
        if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);
        // 确保物体是激活状态才能启动协程
        if (gameObject.activeInHierarchy)
        {
            _breathCoroutine = StartCoroutine(BreathingRoutine(duration));
        }
    }

    private IEnumerator BreathingRoutine(float duration)
    {
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;

            // 使用正弦波 (Sin) 实现极其柔和的呼吸效果，Alpha 在 0.5 到 1.0 之间平滑过渡
            float wave = (Mathf.Sin(Time.time * 4f) + 1f) / 2f;
            _canvasGroup.alpha = Mathf.Lerp(0.4f, 1.0f, wave);

            if (_buttonImage != null)
            {
                _buttonImage.color = Color.Lerp(_originalColor, goldenGlowColor, wave);
            }

            yield return null;
        }

        // 10 秒时间到！用户无视了它
        isBreathing = false;
        SetGhostMode();

        // 【极其重要：记录负样本或零样本，通知 Tracker 用户无视了它】
        Debug.Log($"<color=yellow>[UI]</color> AI 推荐的 {interventionType} 呼吸 10s 结束，用户未理睬 (Ignored)。");
        if (InterventionTracker.Instance != null)
        {
            InterventionTracker.Instance.OnInterventionIgnored(interventionType.ToString());
        }
    }

    // ==========================================
    // 用户点击裁决
    // ==========================================
    private void OnButtonClicked()
    {
        // 停止可能存在的呼吸动画
        if (isBreathing)
        {
            isBreathing = false;
            if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);
            SetGhostMode();
        }

        Debug.Log($"<color=green>[UI]</color> 用户主动点击了按钮: {interventionType}");

        // 通知 Tracker 记录本次点击（用于 ML 训练闭环）
        if (InterventionTracker.Instance != null)
        {
            InterventionTracker.Instance.OnButtonClicked(interventionType.ToString());
        }

        // 【极其重要：接驳到我们刚刚瘦身成功的生成器上！】
        if (ProactiveInterventionSystem.Instance != null)
        {
            ProactiveInterventionSystem.Instance.TriggerInterventionByType(interventionType);
        }
    }
}