using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using Unity.VisualScripting.Antlr3.Runtime.Tree;

public class CoreBodyVisual : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("组件引用")]
    public Outline targetOutline;
    public TMP_InputField targetInput;

    // 必须引用背景图片，用于变色。如果不拖，Awake会自动找。
    public Image targetImage;

    private Color? _overrideColor = null; // 可空类型

    // 控制输入框交互的 CanvasGroup
    private CanvasGroup _inputCanvasGroup;
    // 控制整体透明度的 CanvasGroup
    private CanvasGroup _mainCanvasGroup;

    // 状态：我是否正在被拖拽且准备吸附 (我是子)
    private bool _isChildAdsorbing = false;
    // 状态：我是否是别人的吸附目标 (我是父)
    private bool _isParentTarget = false;
    // 状态：我是否是排序目标 (我是兄弟)
    private bool _isReorderTarget = false;

    // --- 状态公开，供 Controller 读取 ---
    public bool IsEditing { get; private set; } = false;

    private bool _isSelected = false;
    private bool _isHovered = false;

    // 当发生单击时，通知 Controller 去处理选中逻辑
    public event System.Action OnRequestSelection;

    void Awake() // 建议把 GetComponent 放在 Awake
    {
        // 获取或添加用于控制透明度的 CanvasGroup
        _mainCanvasGroup = GetComponent<CanvasGroup>();
        if (_mainCanvasGroup == null) _mainCanvasGroup = gameObject.AddComponent<CanvasGroup>();

        // 2. 获取 Image 并记录初始颜色
        if (targetImage == null) targetImage = GetComponent<Image>();
    }

    void Start()
    {
        if (targetInput != null)
        {
            _inputCanvasGroup = targetInput.GetComponent<CanvasGroup>();
            if (_inputCanvasGroup == null) _inputCanvasGroup = targetInput.gameObject.AddComponent<CanvasGroup>();
            _inputCanvasGroup.blocksRaycasts = false;
            targetInput.onEndEdit.AddListener(OnEndEdit);
            targetInput.onValidateInput += ValidateInput;
        }
        UpdateVisuals();
    }

    // =========================================================
    // 核心拦截逻辑
    // =========================================================
    private char ValidateInput(string text, int charIndex, char addedChar)
    {
        // 如果输入的是换行符 (\n 或 \r)
        if (addedChar == '\n' || addedChar == '\r')
        {
            // 检查是否按住了 Shift 键
            // 逻辑：如果是 Shift+Enter，允许换行；如果是纯 Enter，拦截掉（返回 \0）
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                return '\0'; // 返回空字符，相当于"吞掉"这个输入
            }
        }

        // 其他字符正常放行
        return addedChar;
    }

    public void SetOverrideColor(Color c)
    {
        _overrideColor = c;
        UpdateVisuals(); // 立即刷新
    }

    public void SetGhostMode(bool isGhost)
    {
        if (_mainCanvasGroup != null && !_isChildAdsorbing)
        {
            // 幽灵模式：0.3 透明度；正常模式：1.0 不透明
            _mainCanvasGroup.alpha = isGhost ? 0.3f : 1.0f;
        }
    }

    // 设置“我是子节点，准备吸附”状态
    public void SetChildAdsorbState(bool active)
    {
        if (_isChildAdsorbing != active)
        {
            _isChildAdsorbing = active;
            UpdateVisuals();
        }
    }

    // 设置“我是父节点，被覆盖”状态
    public void SetParentTargetState(bool active)
    {
        if (_isParentTarget != active)
        {
            _isParentTarget = active;
            UpdateVisuals();
        }
    }

    // 设置“我是排序目标”状态
    public void SetReorderTargetState(bool active)
    {
        if (_isReorderTarget != active)
        {
            _isReorderTarget = active;
            // 互斥：如果是排序目标，就不可能是父节点目标
            if (active) _isParentTarget = false;
            UpdateVisuals();
        }
    }

    // 设置选中状态 (Controller -> View)
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateVisuals();
    }

    // --- 交互逻辑 ---

    public void OnPointerClick(PointerEventData eventData)
    {
        if (IsEditing) return;

        // 双击 -> 编辑
        if (eventData.clickCount == 2)
        {
            // 双击通常也意味着选中
            OnRequestSelection?.Invoke();
            EnterEditMode();
        }
        // 单击 -> 请求选中
        else if (eventData.clickCount == 1)
        {
            // 触发事件，让 Controller 去决定是单选还是多选
            OnRequestSelection?.Invoke();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsEditing) { _isHovered = true; UpdateVisuals(); }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovered = false; UpdateVisuals();
    }

    // =========================================================
    // [核心] 统一视觉刷新
    // =========================================================
    // 优先级：正在编辑 > 子节点吸附 > 父节点高亮 > 选中 > 悬停 > 正常
    private void UpdateVisuals()
    {
        if (targetImage == null) return;

        // 尝试获取 ThemeManager，如果没找到则使用默认空值防止报错
        var theme = ThemeManager.Instance != null ? ThemeManager.Instance.currentTheme : null;

        // 如果没有配置主题，使用兜底颜色 (白色)
        Color defaultColor = Color.white;
        // =====================================

        // 1. 默认关闭 Outline
        bool showOutline = false;
        Color outlineColor = Color.white; // 默认边框色

        // 2. 计算目标颜色和透明度
        Color finalColor;
        if (_overrideColor.HasValue)
            finalColor = _overrideColor.Value; // 如果有特殊颜色，优先用特殊的
        else
            finalColor = (theme != null) ? theme.nodeNormal : defaultColor;

        // --- 状态判断逻辑 ---

        if (_isChildAdsorbing)
        {
            // [修改] 状态：我是子节点，且找到了家 -> 变绿 + 透
            if (theme != null)
            {
                finalColor = theme.adsorbSelf;     // 读取主题
            }
            showOutline = true;
        }
        else if (_isParentTarget)
        {
            // [修改] 状态：我是父节点，正被覆盖 -> 变深绿
            if (theme != null)
            {
                finalColor = theme.adsorbTarget;
            }
            showOutline = true;
        }
        else if (_isReorderTarget)
        {
            // [修改] 状态：我是排序目标 -> 变红 (读取新增的 reorderTarget)
            if (theme != null)
            {
                finalColor = theme.reorderTarget;
            }
            showOutline = true;
        }
        else if (_isSelected)
        {
            // [修改] 选中状态
            if (theme != null)
            {
                finalColor = theme.nodeSelected;
                outlineColor = theme.nodeOutline; // 获取主题里的边框色
            }
            showOutline = true;
        }
        else if (_isHovered)
        {
            // [修改] 悬停状态
            if (theme != null)
            {
                finalColor = theme.nodeHover;
            }
            showOutline = true;
        }

        // 3. 应用颜色
        // 注意：这里我们强制 alpha 为 1，因为半透明效果由 CanvasGroup 控制 (或者由 finalColor.a 控制)
        // 如果您的 Theme 颜色里已经调好了 Alpha，这里可以直接用 finalColor
        // 但为了配合下面的 _mainCanvasGroup 逻辑，通常 Image 本身设为不透明，整体透明度交给 Group
        targetImage.color = finalColor;

        // 5. 应用 Outline
        if (targetOutline != null)
        {
            targetOutline.enabled = showOutline;
            // [新增] 如果主题里定义了 outline 颜色，这里也同步修改
            if (showOutline && theme != null)
            {
                targetOutline.effectColor = outlineColor;
            }
        }

        // =========================================================
        // 【新增】：AI 生成认知卡片的视觉弱化 (半透明草稿态)
        // =========================================================
        var baseController = GetComponentInParent<BaseNodeController>();
        if (baseController != null && _mainCanvasGroup != null)
        {
            // 如果是幽灵模式，保持原来的高度透明
            if (_mainCanvasGroup.alpha == 0.3f)
            {
                // do nothing, let SetGhostMode control it
            }
            // 如果是 AI 认知节点，并且还没有被用户“实体化”（即 isCognitiveNode 还是 true）
            else if (baseController.isCognitiveNode)
            {
                // 给予一个半透明的玻璃态视觉暗示（比如 0.65 不透明度）
                _mainCanvasGroup.alpha = 0.4f;

                // 可选：你甚至可以在这里把边框改成虚线，或者把文字颜色调浅
            }
            else
            {
                // 普通节点，完全不透明
                _mainCanvasGroup.alpha = 1.0f;
            }
        }
    }

    // --- 编辑模式逻辑 ---

    public void EnterEditMode()
    {
        if (targetInput != null)
        {
            IsEditing = true;
            if (_inputCanvasGroup != null) _inputCanvasGroup.blocksRaycasts = true;
            targetInput.interactable = true;
            targetInput.ActivateInputField();
            targetInput.Select();
        }
    }

    private void OnEndEdit(string val)
    {
        IsEditing = false;
        if (_inputCanvasGroup != null) _inputCanvasGroup.blocksRaycasts = false;
        if (gameObject.activeInHierarchy) StartCoroutine(DisableInteractableNextFrame());
        ClearOverrideColor();
    }

    IEnumerator DisableInteractableNextFrame()
    {
        yield return null;
        if (targetInput != null) targetInput.interactable = false;
    }

    public void ClearOverrideColor()
    {
        // 只有当前确实有覆盖色时才执行，避免重复刷新
        if (_overrideColor.HasValue)
        {
            _overrideColor = null;
            UpdateVisuals(); // 立即刷新，让它变回白色或选中色
        }
    }
}