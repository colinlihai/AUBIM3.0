using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button), typeof(Image))]
public class UnifiedButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("统一颜色配置")]
    public Color normalColor = new Color(0.4f, 0.4f, 0.4f, 1f); // 浅灰 (可根据你的UI微调)
    public Color hoverColor = new Color(0.3f, 0.3f, 0.3f, 1f); // 深灰

    private Image _img;
    private Button _btn;
    private bool _isHovering = false;

    void Awake()
    {
        _img = GetComponent<Image>();
        _btn = GetComponent<Button>();

        // 禁用 Unity 原生颜色过渡，防止与发光脚本抢夺渲染权
        _btn.transition = Selectable.Transition.None;
        _img.color = normalColor;
    }

    public void OnPointerEnter(PointerEventData eventData) => _isHovering = true;
    public void OnPointerExit(PointerEventData eventData) => _isHovering = false;

    void Update()
    {
        // 1. 如果当前正在被 AI 闪烁发光，绝对不干涉颜色！交由 Copilot 控制
        if (IsGlowing()) return;

        // 2. 如果按钮当前正处于功能选中状态 (比如被点击后变成了黄色的待命状态)，也不干涉！
        if (IsActiveTool()) return;

        // 3. 正常状态下，根据鼠标悬停进行实时平滑变色
        Color targetColor = _isHovering ? hoverColor : normalColor;

        // 使用 Lerp 让浅灰到深灰的过渡更显高级和顺滑
        _img.color = Color.Lerp(_img.color, targetColor, Time.deltaTime * 10f);
    }

    // 向中枢询问是否在发光
    private bool IsGlowing()
    {
        if (CopilotActionController.Instance != null)
            return CopilotActionController.Instance.IsButtonCurrentlyGlowing(gameObject);
        return false;
    }

    // 向中枢询问是否是当前被选中的工具
    private bool IsActiveTool()
    {
        if (CopilotActionController.Instance != null)
            return CopilotActionController.Instance.IsButtonActiveTool(gameObject);
        return false;
    }
}