using UnityEngine;
using UnityEngine.UI;
using System.Collections;
public enum DrawerSide
{
    Right,  // 右侧抽屉 (HarvestArea)
    Left,   // 左侧抽屉 (AIChatArea)
    Top,    // (预留)
    Bottom  // (预留)
}

public class SidePanelDrawer : MonoBehaviour
{
    [Header("配置")]
    public DrawerSide side = DrawerSide.Right; // [关键] 在 Inspector 里选方向
    public bool isOpen = true; // 默认状态

    [Header("组件引用")]
    public RectTransform panelRect;  // 面板本体
    public Button toggleButton;      // 把手按钮
    public Transform arrowIcon;      // 按钮上的箭头图标

    [Header("动画参数")]
    public float slideDuration = 0.3f;
    public AnimationCurve slideCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("图标旋转设置 (Z轴角度)")]
    // 默认假设：右侧抽屉展开时箭头指向右(0)，折叠时指向左(180)
    // 左侧抽屉通常相反，你可以在 Inspector 里调整这两个值
    public float iconAngleOpen = 0f;
    public float iconAngleClosed = 180f;

    // 内部变量
    private float _expandedPos;  // 展开时的坐标
    private float _collapsedPos; // 折叠时的坐标
    private Coroutine _currentAnim;

    void Start()
    {
        if (panelRect == null) panelRect = GetComponent<RectTransform>();

        // 1. 自动计算坐标
        // 我们以编辑器里摆放好的位置作为 "展开状态" (_expandedPos)
        // 然后根据方向和宽度计算 "折叠状态" (_collapsedPos)

        if (side == DrawerSide.Right || side == DrawerSide.Left)
        {
            _expandedPos = panelRect.anchoredPosition.x;
            float width = panelRect.rect.width;

            if (side == DrawerSide.Right)
            {
                // 右侧抽屉：折叠时 X 变大 (向右移出屏幕)
                _collapsedPos = _expandedPos + width;
            }
            else // Left
            {
                // 左侧抽屉：折叠时 X 变小 (向左移出屏幕)
                _collapsedPos = _expandedPos - width;
            }
        }
        else
        {
            // 如果未来要做上下抽屉，逻辑类似 (操作 Y 轴)
            _expandedPos = panelRect.anchoredPosition.y;
            // ... Y轴逻辑
        }

        // 2. 绑定按钮
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveAllListeners();
            toggleButton.onClick.AddListener(ToggleState);
        }

        // 3. 初始化位置
        SetStateImmediate(isOpen);
    }

    public void ToggleState()
    {
        if (_currentAnim != null) StopCoroutine(_currentAnim);
        _currentAnim = StartCoroutine(AnimatePanel(!isOpen));
    }

    public void SetStateImmediate(bool open)
    {
        isOpen = open;
        float target = isOpen ? _expandedPos : _collapsedPos;

        SetPosition(target);
        UpdateIconRotation();
    }

    private IEnumerator AnimatePanel(bool targetOpen)
    {
        isOpen = targetOpen;

        // 获取当前值
        float startVal = (side == DrawerSide.Right || side == DrawerSide.Left)
            ? panelRect.anchoredPosition.x
            : panelRect.anchoredPosition.y;

        float targetVal = isOpen ? _expandedPos : _collapsedPos;

        float timer = 0f;
        while (timer < slideDuration)
        {
            timer += Time.deltaTime;
            float t = timer / slideDuration;
            float curveT = slideCurve.Evaluate(t);

            float currentVal = Mathf.Lerp(startVal, targetVal, curveT);
            SetPosition(currentVal);

            yield return null;
        }

        SetPosition(targetVal);
        UpdateIconRotation();
    }

    // 辅助：统一设置坐标
    private void SetPosition(float value)
    {
        Vector2 pos = panelRect.anchoredPosition;
        if (side == DrawerSide.Right || side == DrawerSide.Left)
            pos.x = value;
        else
            pos.y = value;
        panelRect.anchoredPosition = pos;
    }

    private void UpdateIconRotation()
    {
        if (arrowIcon != null)
        {
            float targetZ = isOpen ? iconAngleOpen : iconAngleClosed;
            arrowIcon.rotation = Quaternion.Euler(0, 0, targetZ);
        }
    }

    public void OpenDrawer()
    {
        if (!isOpen) ToggleState();
    }

    public void CloseDrawer()
    {
        if (isOpen) ToggleState();
    }
}