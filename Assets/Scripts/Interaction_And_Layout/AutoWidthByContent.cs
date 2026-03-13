using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(LayoutElement))]
public class AutoWidthByContent : MonoBehaviour
{
    [Header("配置")]
    public float minWidth = 200f;   // 默认最小宽度
    public float maxWidth = 500f;   // 最大宽度 (超过这个就开始换行)
    public float padding = 50f;     // 左右内边距补正 (建议稍微给大一点，给光标留位置)

    [Header("引用")]
    public TMP_InputField targetInput; // 必须拖拽引用

    public bool isAutoWidth = true;

    private LayoutElement _layoutElement;
    private RectTransform _rectTransform;

    void Awake()
    {
        _layoutElement = GetComponent<LayoutElement>();
        _rectTransform = GetComponent<RectTransform>();
    }

    void Start()
    {
        if (targetInput != null)
        {
            targetInput.onValueChanged.AddListener(OnTextChanged);
            // 初始化
            OnTextChanged(targetInput.text);
        }
    }

    public void OnTextChanged(string text)
    {
        if (!isAutoWidth) return;
        if (targetInput == null || targetInput.textComponent == null) return;

        // [核心修复]
        // 不读取当前的 preferredWidth，而是请求 TMP 计算"如果不换行，这段文字有多宽"
        // float.PositiveInfinity 是关键，告诉 TMP 假设宽度无限大
        Vector2 idealSize = targetInput.textComponent.GetPreferredValues(text, float.PositiveInfinity, float.PositiveInfinity);

        // 1. 获取单行理论宽度
        float singleLineWidth = idealSize.x;

        // 2. 加上 Padding (左右留白 + 光标位置)
        float targetWidth = singleLineWidth + padding;

        // 3. 智能判断
        // 如果 < 200 -> 保持 200
        // 如果 200 ~ 600 -> 变宽
        // 如果 > 600 -> 锁死 600 (此时 InputField 会因为空间不够而自动换行)
        float finalWidth = Mathf.Clamp(targetWidth, minWidth, maxWidth);

        // 4. 应用宽度
        // 只有变化较大时才应用，避免浮点数抖动
        if (Mathf.Abs(_layoutElement.preferredWidth - finalWidth) > 1f)
        {
            _layoutElement.preferredWidth = finalWidth;

            // 5. 强制刷新，确保立刻生效，防止文字先换行再变宽
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rectTransform);

            // 这一步很重要：通知 InputField 重新计算文字排版
            targetInput.textComponent.SetAllDirty();
        }
    }

    // 供外部调用
    public void ForceUpdate()
    {
        if (targetInput != null) OnTextChanged(targetInput.text);
    }
}