using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChatMessageController : MonoBehaviour
{
    [Header("UI 引用")]
    public TMP_InputField messageInput;
    public Image bubbleBackground;

    public Button deleteButton;
    public Button extractBtn;

    public void Setup(string content, bool isUser)
    {
        messageInput.text = content;
        var theme = ThemeManager.Instance != null ? ThemeManager.Instance.currentTheme : null;
        if (isUser)
        {
            // 用户配色
            if (theme != null)
            {
                if (bubbleBackground != null) bubbleBackground.color = theme.chatUserBubble;
                if (messageInput.textComponent != null) messageInput.textComponent.color = theme.chatUserText;
            }
            else
            {
                // 兜底默认值 (蓝色背景，白字)
                if (bubbleBackground != null) bubbleBackground.color = new Color(0.2f, 0.6f, 1f);
                if (messageInput.textComponent != null) messageInput.textComponent.color = Color.white;
            }
        }
        else
        {
            // AI 配色
            if (theme != null)
            {
                if (bubbleBackground != null) bubbleBackground.color = theme.chatAIBubble;
                if (messageInput.textComponent != null) messageInput.textComponent.color = theme.chatAIText;
            }
            else
            {
                // 兜底默认值 (深灰背景，白字) - 或者您习惯的浅灰背景黑字
                if (bubbleBackground != null) bubbleBackground.color = new Color(0.9f, 0.9f, 0.9f);
                if (messageInput.textComponent != null) messageInput.textComponent.color = Color.black;
            }
        }

        if (extractBtn != null)
        {
            extractBtn.onClick.RemoveAllListeners();
            extractBtn.onClick.AddListener(() =>
            {
                if (ToastSystem.Instance != null) ToastSystem.Instance.Show("AI 正在解构文本语义...");

                // 2. 【核心新增】记录用户的“提取意图”埋点
                if (UserBehaviorSystem.Instance != null)
                {
                    UserBehaviorSystem.Instance.LogEvent(
                        BehaviorEventType.AI_Chat_ExtractClick,
                        targetID: "ChatBubble",
                        info: "Request_Extraction",
                        value: content.Length // 记录一下用户觉得多长的文本有提取价值，非常好的研究数据！
                    );
                }

                Vector2 centerPos = Vector2.zero; // 或者您计算的鼠标松开位置

                // 1. 先呼叫 AITaskAssistant 处理语义和括号
                AITaskAssistant.Instance.ExtractTextToTreeData(content, (treeData) =>
                {
                    if (treeData != null)
                    {
                        // 2. 拿到完美 JSON 后，丢给 NodeCardManager 去画图
                        NodeCardManager.Instance.BuildTreeFromAIData(treeData, centerPos);
                    }
                    else
                    {
                        if (ToastSystem.Instance != null) ToastSystem.Instance.Show("语义解构失败");
                    }
                });
            });
        }

        // [关键] 强制刷新布局，确保气泡大小立刻适配文字
        // 有时候需要延迟一帧，这里先强制调用
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }
}