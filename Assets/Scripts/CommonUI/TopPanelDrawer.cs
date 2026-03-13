using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class TopPanelDrawer : MonoBehaviour
{
    [Header("组件引用")]
    public RectTransform drawerPanel;      // FileArea 自身
    public RectTransform headerRect;       // 你的把手 (Header)
    public LayoutElement listLayoutElement; // FileListContainer 上的 LayoutElement (用于控制它的高度)
    public RectTransform listContent;      // FileListContainer/Viewport/Content (用于获取真实内容高度)
    public RectTransform arrowIcon;        // Header 上的箭头图标

    [Header("高度设置")]
    public float maxListHeight = 400f;     // 列表最大高度，超过这个高度就出滚动条
    public float slideDuration = 0.3f;
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // 状态
    private bool _isOpen = false;
    private Coroutine _animCoroutine;

    void Start()
    {
        if (drawerPanel == null) drawerPanel = GetComponent<RectTransform>();

        // 初始强制关闭
        // 注意：先计算一次高度，确保 UI 状态正确
        StartCoroutine(InitLayout());
    }

    // 初始化协程：等待 UI Layout 构建一帧后再计算
    private IEnumerator InitLayout()
    {
        yield return new WaitForEndOfFrame();
        RefreshHeight();
        SetStateImmediate(false);
    }

    // --- 核心功能 1: 动态高度控制 ---
    // 这个方法应该在每次 RefreshFileList 后调用
    public void RefreshHeight()
    {
        // 1. 强制刷新 Content 的布局，获取最新高度
        LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);

        float contentHeight = listContent.rect.height;

        // 2. 计算目标高度：取 内容高度 和 最大限制 中的较小值
        // 如果内容很少(比如 100)，高度就是 100；如果内容很多(800)，高度就是 400
        float targetHeight = Mathf.Min(contentHeight, maxListHeight);

        // 3. 应用给 ScrollView 的 LayoutElement
        listLayoutElement.preferredHeight = targetHeight;

        // 4. 刷新父物体 (FileArea) 的 ContentSizeFitter，让它重新计算整体尺寸
        LayoutRebuilder.ForceRebuildLayoutImmediate(drawerPanel);

        // 5. 如果当前是关闭状态，需要重新校准位置 (因为整体高度变了，关闭时的 Y 坐标也得变)
        if (!_isOpen)
        {
            SetStateImmediate(false);
        }
    }

    // --- 核心功能 2: 抽屉动画 ---
    public void ToggleDrawer()
    {
        _isOpen = !_isOpen;
        if (_animCoroutine != null) StopCoroutine(_animCoroutine);
        _animCoroutine = StartCoroutine(AnimatePanel(_isOpen));
    }

    public void CloseDrawer()
    {
        if (_isOpen) ToggleDrawer();
    }

    public void SetStateImmediate(bool open)
    {
        _isOpen = open;
        float targetY = CalculateTargetY(open);
        drawerPanel.anchoredPosition = new Vector2(drawerPanel.anchoredPosition.x, targetY);
        UpdateArrow();
    }

    private IEnumerator AnimatePanel(bool targetOpen)
    {
        float startY = drawerPanel.anchoredPosition.y;
        float targetY = CalculateTargetY(targetOpen);
        float timer = 0f;

        while (timer < slideDuration)
        {
            timer += Time.deltaTime;
            float t = timer / slideDuration;
            float curveT = slideCurve.Evaluate(t);

            float currentY = Mathf.Lerp(startY, targetY, curveT);
            drawerPanel.anchoredPosition = new Vector2(drawerPanel.anchoredPosition.x, currentY);

            yield return null;
        }

        drawerPanel.anchoredPosition = new Vector2(drawerPanel.anchoredPosition.x, targetY);
        UpdateArrow();
    }

    // 计算 Y 轴坐标 (基于 Pivot Top = 1)
    private float CalculateTargetY(bool open)
    {
        // Pivot Y = 1 (顶部对齐)
        // 坐标系：Y 向上为正。
        // Open (展开): Panel 顶部对齐屏幕顶部 -> Y = 0
        // Closed (收起): Panel 向上移动，只露出底部的 Header。
        // 需要向上移动的距离 = List部分的高度 = (TotalHeight - HeaderHeight)

        if (open)
        {
            return 0f;
        }
        else
        {
            float listHeight = listLayoutElement.preferredHeight;
            // 向上移是正方向
            return listHeight;
        }
    }

    private void UpdateArrow()
    {
        if (arrowIcon != null)
        {
            // 展开时箭头向上(180)，折叠时箭头向下(0)
            float targetZ = _isOpen ? 180f : 0f;
            arrowIcon.rotation = Quaternion.Euler(0, 0, targetZ);
        }
    }
}