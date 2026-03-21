using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;

[RequireComponent(typeof(Button))]
[RequireComponent(typeof(Image))]
public class GlobalProactiveButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static GlobalProactiveButton Instance;

    private Button _btn;
    private Image _btnImage;
    private LayoutElement _layoutElement;
    private Coroutine _breathCoroutine;

    [Header("状态监控")]
    public bool isBreathing = false;
    private InterventionType _pendingType;
    private bool _isHovering = false;
    private bool _isVisible = true;

    // ==========================================
    // 视觉配置 (去除了半透明幽灵态，改为标准实心UI)
    // ==========================================
    private Color normalColor = new Color(0.4f, 0.4f, 0.4f, 1f); 
    private Color hoverColor = new Color(0.3f, 0.3f, 0.3f, 1f);  // 悬停态：微微变暗
    private Color glowColor = new Color(1f, 0.8f, 0.2f, 1f);     // 闪烁时：耀眼金色

    void Awake()
    {
        Instance = this;
        _btn = GetComponent<Button>();
        _btnImage = GetComponent<Image>();
        _btnImage.color = normalColor;

        // 自动获取或添加 LayoutElement，这是在 VerticalLayoutGroup 中动态折叠隐藏的神器
        _layoutElement = GetComponent<LayoutElement>();
        if (_layoutElement == null) _layoutElement = gameObject.AddComponent<LayoutElement>();

        _btn.onClick.AddListener(OnButtonClicked);
    }

    void Update()
    {
        // ==========================================
        // 动态监控画布节点数量，大于 5 个才显示这个按钮
        // ==========================================
        if (NodeCardManager.Instance != null)
        {
            int nodeCount = NodeCardManager.Instance.GetAllNodes().Count;
            bool isArticleActive = ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy;

            // 只有当“成文区未打开”且“节点大于5个”时，全局思考按钮才显示！
            bool shouldShow = !isArticleActive && nodeCount > 5;

            if (_isVisible != shouldShow)
            {
                SetVisibility(shouldShow);
            }
        }

        // 【新增】：平滑的悬停变色逻辑 (与成文区按钮保持完美的视觉统一)
        if (_isVisible && !isBreathing)
        {
            Color targetColor = _isHovering ? hoverColor : normalColor;
            _btnImage.color = Color.Lerp(_btnImage.color, targetColor, Time.deltaTime * 10f);
        }
    }

    /// <summary>
    /// 控制按钮在 LayoutGroup 中的智能显隐
    /// </summary>
    private void SetVisibility(bool show)
    {
        _isVisible = show;

        // 核心：控制 LayoutElement 忽略排版，这样它就会在 LayoutGroup 中自动折叠，完全不占空间
        if (_layoutElement != null) _layoutElement.ignoreLayout = !show;

        // 关闭 Image 和 Button 组件使其不可见且不可交互
        _btnImage.enabled = show;
        _btn.enabled = show;

        // 关闭它下面的文字、图标等子物体
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(show);
        }

        // 如果被隐藏了，强制打断可能正在进行的闪烁
        if (!show && isBreathing)
        {
            StopBreathingEarly();
        }
    }

    // ==========================================
    // 鼠标悬停事件侦测
    // ==========================================
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_isVisible) return; // 隐藏状态下不响应

        _isHovering = true;
        if (!isBreathing)
        {
            _btnImage.color = hoverColor;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isVisible) return;

        _isHovering = false;
        if (!isBreathing)
        {
            _btnImage.color = normalColor;
        }
    }

    // ==========================================
    // 供 InterventionTracker (大脑) 呼叫
    // ==========================================
    public void StartGlobalBreathing(InterventionType type)
    {
        if (isBreathing || !_isVisible) return; // 隐藏状态下严禁闪烁！

        isBreathing = true;
        _pendingType = type;
        if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);
        _breathCoroutine = StartCoroutine(BreathRoutine());

        Debug.Log($"<color=yellow>[AI 介入]</color> 全局反思按钮开始闪烁，限时 10 秒等待采纳...");
    }

    public void StopBreathingEarly()
    {
        if (isBreathing)
        {
            isBreathing = false;
            if (_breathCoroutine != null) StopCoroutine(_breathCoroutine);

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
    // 带 10 秒超时检测的呼吸协程
    // ==========================================
    private IEnumerator BreathRoutine()
    {
        float timer = 0f;
        float elapsed = 0f;

        // 10 秒存活判定
        while (isBreathing && elapsed < 10f)
        {
            timer += Time.deltaTime * 2f;
            elapsed += Time.deltaTime;

            float lerp = (Mathf.Sin(timer) + 1f) / 2f;

            Color baseColor = _isHovering ? hoverColor : normalColor;
            _btnImage.color = Color.Lerp(baseColor, glowColor, lerp);

            yield return null;
        }

        // 如果 10 秒走完，依然没有被点击或打断
        if (isBreathing)
        {
            isBreathing = false;
            _btnImage.color = _isHovering ? hoverColor : normalColor;

            // 【向 ML 模型汇报】：用户无视了呼唤
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionIgnored(_pendingType.ToString());
            }

            Debug.Log($"<color=yellow>[AI 介入]</color> 10 秒已过，用户无视了全局建议，按钮自动熄灭 (-0.2分)。");
        }
    }
}