using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(Button))]
public class ChatSuggestionChip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TMP_Text textComponent;
    public CanvasGroup canvasGroup;
    public Button button;

    private string _shortTitle;
    private string _fullContent;
    private TMP_InputField _targetPromptInput;

    public void Initialize(string shortTitle, string fullContent, TMP_InputField targetInput)
    {
        _shortTitle = shortTitle;
        _fullContent = fullContent;
        _targetPromptInput = targetInput;

        if (textComponent != null) textComponent.text = _shortTitle;

        // 初始透明度为 0，等待管理器用协程淡入
        canvasGroup.alpha = 0f;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnChipClicked);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 1. 视觉提亮
        canvasGroup.alpha = 1f;

        // =========================================================
        // 【交互引导 1：富文本微暗示】
        // 在长句子的末尾，无缝拼接一个带有颜色、斜体、略小字号的行动号召 (CTA)
        // 使用 TextMeshPro 的富文本标签，无需增加任何额外 UI 节点！
        // =========================================================
        if (textComponent != null)
        {
            string callToAction = " <color=#FFD700><i>[点击填入]</i></size></color>";
            textComponent.text = _fullContent + callToAction;
        }

        // =========================================================
        // 【交互引导 2：物理反馈微动效】
        // 让气泡极其轻微地放大 2%，产生一种“被激活”、“浮起来”的按钮质感
        // =========================================================
        transform.localScale = new Vector3(1.02f, 1.02f, 1.02f);

        // 通知管理器锁定计时
        if (ChatSuggestionManager.Instance != null)
        {
            ChatSuggestionManager.Instance.SetHoverState(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 1. 恢复半透明与短标题
        canvasGroup.alpha = 0.8f;
        if (textComponent != null) textComponent.text = _shortTitle;

        // 2. 恢复原始大小
        transform.localScale = Vector3.one;

        // 通知管理器恢复计时
        if (ChatSuggestionManager.Instance != null)
        {
            ChatSuggestionManager.Instance.SetHoverState(false);
        }
    }

    private void OnChipClicked()
    {
        if (_targetPromptInput != null)
        {
            // 1. 填入完整内容并聚焦
            _targetPromptInput.text = _fullContent;
            _targetPromptInput.ActivateInputField();
        }

        // 2. 呼叫管理器：开始后台监控用户的修改和发送行为！
        if (ChatSuggestionManager.Instance != null)
        {
            ChatSuggestionManager.Instance.TrackCoCreationSubmission(_fullContent);
            ChatSuggestionManager.Instance.ClearAllChips();
        }
    }
}