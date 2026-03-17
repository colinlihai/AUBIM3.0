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
        // 悬停：变为不透明，并显示完整句子
        canvasGroup.alpha = 1f;
        if (textComponent != null) textComponent.text = _fullContent;
        if (ChatSuggestionManager.Instance != null)
        {
            ChatSuggestionManager.Instance.SetHoverState(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // 离开：恢复半透明，显示简短标题
        canvasGroup.alpha = 0.8f; // 设定的半透明值
        if (textComponent != null) textComponent.text = _shortTitle;
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