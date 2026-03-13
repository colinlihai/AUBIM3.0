using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class AIChatManager : MonoBehaviour
{
    public static AIChatManager Instance;

    [Header("UI 组件")]
    public Transform contentContainer;
    public TMP_InputField chatInput;
    public ScrollRect scrollRect;

    [Header("Prefab")]
    public GameObject messagePrefab;

    // UI 层的显示列表（仅用于 SaveSystem 存读档和 UI 重建）
    private List<ChatMessageData> _allMessages = new List<ChatMessageData>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (chatInput != null)
        {
            // 【新增：锁定动作 A】点击输入框准备打字时上锁
            chatInput.onSelect.AddListener((val) => {
                if (InterventionTracker.Instance != null) InterventionTracker.Instance.SuspendByChat();
            });

            chatInput.onSubmit.AddListener((val) => {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;
                if (!string.IsNullOrWhiteSpace(val)) OnSendClicked();
            });
        }
    }

    public void OnSendClicked()
    {
        string text = chatInput.text.TrimEnd('\r', '\n', ' ');
        if (string.IsNullOrWhiteSpace(text)) return;

        // 【新增：锁定动作 B】发送消息并等待回复时，继续保持锁定状态
        if (InterventionTracker.Instance != null) InterventionTracker.Instance.SuspendByChat();

        // 1. 组装数据并记录到本地 UI 数据列表 (用于存盘)
        var userMsg = new ChatMessageData { role = "user", content = text, timestamp = System.DateTime.Now.ToString() };
        _allMessages.Add(userMsg);

        // 2. UI 立刻显示用户的问题 (传入数据对象绑定)
        AddBubbleUI(userMsg, true);

        // 3. 防空行清空
        StartCoroutine(ResetInputNextFrame());

        // [埋点]
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Chat_Query, "User", "Chat", text.Length);
        }

        // 4. 发送给 LLMManager
        StartCoroutine(RequestAI(text));
    }

    private System.Collections.IEnumerator ResetInputNextFrame()
    {
        // 关键：等待这一帧结束，让 TMP 把底层的物理回车事件彻底消耗掉
        yield return null;

        if (chatInput != null)
        {
            chatInput.text = "";
            chatInput.ActivateInputField(); // 重新获取焦点

            // 强制刷新输入框布局
            LayoutRebuilder.ForceRebuildLayoutImmediate(chatInput.GetComponent<RectTransform>());
        }
    }

    private IEnumerator RequestAI(string userPrompt)
    {
        if (LLMManager.Instance != null)
        {
            // 调用核心 Chat 接口
            LLMManager.Instance.Chat(userPrompt, (response, success) =>
            {
                if (success)
                {
                    // 【核心修复】：如果 AI 仅仅输出了后台指令，剥离后剩下的是空文本，就直接跳过，不生成空白气泡！
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        // 记录到本地 UI 数据列表
                        var aiMsg = new ChatMessageData { role = "assistant", content = response, timestamp = System.DateTime.Now.ToString() };
                        _allMessages.Add(aiMsg);

                        // UI 显示 AI 回复并绑定数据
                        AddBubbleUI(aiMsg, false);

                        // [埋点]
                        if (UserBehaviorSystem.Instance != null)
                        {
                            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Chat_Response, "AI", "Reply", response.Length);
                        }
                    }
                }
                else
                {
                    // 错误信息也包装成 Data 显示，但不存入 LLM 历史
                    var errMsg = new ChatMessageData { role = "assistant", content = $"[错误] {response}", timestamp = System.DateTime.Now.ToString() };
                    AddBubbleUI(errMsg, false);
                }
            });

            yield return null;
        }
        else
        {
            Debug.LogError("LLMManager instance not found");
            var missingMsg = new ChatMessageData { role = "assistant", content = "错误：LLMManager 未初始化", timestamp = System.DateTime.Now.ToString() };
            AddBubbleUI(missingMsg, false);
        }
    }

    // ==========================================
    // 气泡生成与销毁核心逻辑
    // ==========================================

    private void AddBubbleUI(ChatMessageData msgData, bool isUser)
    {
        if (messagePrefab == null) return;

        GameObject obj = Instantiate(messagePrefab, contentContainer);
        var ctrl = obj.GetComponent<ChatMessageController>();

        if (ctrl != null)
        {
            ctrl.Setup(msgData.content, isUser);

            // 【核心修改】：接管气泡上的专属删除按钮
            if (ctrl.deleteButton != null)
            {
                // 先清空一下，防止重复绑定
                ctrl.deleteButton.onClick.RemoveAllListeners();

                // 将删除按钮与外层的 DeleteChatBubble 逻辑绑定
                ctrl.deleteButton.onClick.AddListener(() => {
                    DeleteChatBubble(obj, msgData);
                });
            }
            else
            {
                Debug.LogWarning("[UI] 气泡 Prefab 上的 DeleteButton 没有赋值或丢失！");
            }
        }

        StartCoroutine(ScrollToBottom());
    }

    // [新增] 处理气泡销毁与三端数据同步
    private void DeleteChatBubble(GameObject bubbleObj, ChatMessageData msgData)
    {
        // 1. 从本地存盘数据中剔除
        if (_allMessages.Contains(msgData))
        {
            _allMessages.Remove(msgData);
        }

        // 2. 从大模型的上下文中剔除 (彻底断绝它的记忆)
        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.RemoveFromHistory(msgData.content);
        }

        // 3. 精准埋点记录
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.AI_Chat_DeleteBubble,
                targetID: msgData.role,
                info: "ContextCuration",
                value: msgData.content.Length
            );
            Debug.Log($"<color=orange>[追踪]</color> 用户修剪了上下文！删除了 {msgData.role} 的一条记录。");
        }

        // 4. 物理销毁 UI 节点
        Destroy(bubbleObj);

        // 5. 延迟一帧强制刷新列表排版，让气泡自动向上排列补齐空缺
        StartCoroutine(RebuildChatLayoutNextFrame());
    }

    private System.Collections.IEnumerator RebuildChatLayoutNextFrame()
    {
        yield return null;
        if (contentContainer != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer.GetComponent<RectTransform>());
        }
    }

    IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentContainer.GetComponent<RectTransform>());
        yield return new WaitForEndOfFrame();
        scrollRect.verticalNormalizedPosition = 0f;
    }

    // ==========================================
    // SaveSystem 接口
    // ==========================================

    public List<ChatMessageData> GetHistoryData()
    {
        return _allMessages;
    }

    public void LoadHistoryData(List<ChatMessageData> history)
    {
        ClearChatSession(); // 先清空

        if (history != null)
        {
            _allMessages = new List<ChatMessageData>(history);
            foreach (var msg in _allMessages)
            {
                // 加载存档时，同样将 msg 数据对象直接传进去绑定
                AddBubbleUI(msg, msg.role == "user");
            }

            SyncHistoryToLLM();
        }
    }

    public void ClearChatSession()
    {
        _allMessages.Clear();
        if (contentContainer != null)
        {
            foreach (Transform child in contentContainer) Destroy(child.gameObject);
        }

        if (LLMManager.Instance != null) LLMManager.Instance.ClearHistory();
    }

    private void SyncHistoryToLLM()
    {
        if (LLMManager.Instance == null) return;
        LLMManager.Instance.ClearHistory();
        LLMManager.Instance.RestoreHistory(_allMessages);
    }
}