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

    [Header("AUBIM 4.0 上下文挂载开关")]
    public UnityEngine.UI.Toggle toggleMountTree;    // 挂载全局导图开关
    public UnityEngine.UI.Toggle toggleMountArticle; // 挂载正文内容开关

    [Header("Prefab")]
    public GameObject messagePrefab;

    // UI 层的显示列表（仅用于 SaveSystem 存读档和 UI 重建）
    private List<ChatMessageData> _allMessages = new List<ChatMessageData>();

    public bool IsChatInputFocused()
    {
        return chatInput != null && chatInput.isFocused;
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (chatInput != null)
        {
            // 【锁定动作 A】点击输入框准备打字时上锁
            chatInput.onSelect.AddListener((val) => {
                if (InterventionTracker.Instance != null) InterventionTracker.Instance.SuspendByChat();
                // 【新增：广播输入框获得焦点，Copilot 将据此生成引用气泡】
                if (CopilotActionController.Instance != null) CopilotActionController.Instance.OnChatInputSelected();
            });

            chatInput.onSubmit.AddListener((val) => {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return;
                OnSendClicked();
            });
        }
    }

    public void OnSendClicked()
    {
        string text = chatInput.text.TrimEnd('\r', '\n', ' ');

        // 【修复 2】：先让 Copilot 中枢看一眼！如果它正在待命，空文本也会被它截获去执行默认 Prompt！
        bool intercepted = false;
        if (CopilotActionController.Instance != null)
        {
            intercepted = CopilotActionController.Instance.TryInterceptChatSubmit(text);
        }

        // 如果 Copilot 拦截了（吃掉了这次回车），直接清空输入框并结束，不要走下面普通的闲聊逻辑
        if (intercepted)
        {
            StartCoroutine(ResetInputNextFrame());
            return;
        }

        // ==========================================
        // 下面是普通的 AI 闲聊逻辑 (用户正常问问题)
        // ==========================================

        // 【修复 3】：如果不是 Copilot 工具模式，普通聊天依旧拦截纯空格和空行
        if (string.IsNullOrWhiteSpace(text)) return;

        // 保持锁定
        if (InterventionTracker.Instance != null) InterventionTracker.Instance.SuspendByChat();

        // 1. 组装数据并显示气泡
        var userMsg = new ChatMessageData { role = "user", content = text, timestamp = System.DateTime.Now.ToString() };
        _allMessages.Add(userMsg);
        AddBubbleUI(userMsg, true);

        // 2. 清空输入框
        StartCoroutine(ResetInputNextFrame());

        // 3. 埋点
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Chat_Query, "User", "Chat", text.Length);
        }

        // 4. 发起请求
        StartCoroutine(RequestAI(text));
    }

    private IEnumerator ResetInputNextFrame()
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
            // 组装动态上下文
            System.Text.StringBuilder dynamicContext = new System.Text.StringBuilder();

            // 检查导图开关是否打开
            if (toggleMountTree != null && toggleMountTree.isOn && ProjectContextGatherer.Instance != null)
            {
                dynamicContext.AppendLine("【用户提供的参考资料 1：当前全局思维导图结构】");
                dynamicContext.AppendLine(ProjectContextGatherer.Instance.GetTreeStructureContext_Public());
                dynamicContext.AppendLine("------------------");
            }

            // 检查正文开关是否打开
            if (toggleMountArticle != null && toggleMountArticle.isOn && ArticleGenerator.Instance != null && ArticleGenerator.Instance.mainBodyInput != null)
            {
                dynamicContext.AppendLine("【用户提供的参考资料 2：当前正文内容】");
                dynamicContext.AppendLine(ArticleGenerator.Instance.mainBodyInput.text);
                dynamicContext.AppendLine("------------------");
            }

            string contextStr = dynamicContext.ToString();

            // 调用刚刚写好的动态上下文聊天接口
            LLMManager.Instance.ChatWithDynamicContext(userPrompt, contextStr, (response, success) =>
            {
                if (success && !string.IsNullOrWhiteSpace(response))
                {
                    response = FormatAIResponse(response);

                    var aiMsg = new ChatMessageData { role = "assistant", content = response, timestamp = System.DateTime.Now.ToString() };
                    _allMessages.Add(aiMsg);
                    AddBubbleUI(aiMsg, false);
                    // [埋点]
                    if (UserBehaviorSystem.Instance != null)
                        UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Chat_Response, "AI", "Reply", response.Length);

                    if (InterventionTracker.Instance != null)
                    {
                        InterventionTracker.Instance.ResumeFromChat();
                        InterventionTracker.Instance.GrantReadingBuffer(response.Length);
                    }
                    if (LLMManager.Instance != null)
                    {
                        LLMManager.Instance.GenerateSocraticQuestionsInBackground(userPrompt, response);
                    }
                }
                else if (!success)
                {
                    var errMsg = new ChatMessageData { role = "assistant", content = $"[错误] {response}", timestamp = System.DateTime.Now.ToString() };
                    AddBubbleUI(errMsg, false);

                    if (InterventionTracker.Instance != null) InterventionTracker.Instance.ResumeFromChat();
                }
            });
            yield return null;
        }
    }

    // ==========================================
    // 文本格式化与清洗过滤器
    // ==========================================
    private string FormatAIResponse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "";

        string cleaned = rawText;
        cleaned = cleaned.Replace("\u200B", "").Replace("\uFEFF", "").Replace("\u200C", "").Replace("\u200D", "");
        // 1. 去除无意义的 Markdown 标题符号 (例如 "### 标题" 变成单纯的 "标题")
        // (?m) 代表多行匹配模式，^ 代表行首，#+ 代表多个#号，\s* 代表后面的空格
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?m)^#+\s*", "");
        cleaned = cleaned.Replace("**", "");
        // 2. 去除灾难级的空行：将 3 个或以上连续的换行符（包含夹杂的空格），强行压缩为 2 个换行符 (保留正常的段落间距)
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(\r?\n\s*){2,}", "\n\n");

        // 3. 剔除首尾多余的留白
        return cleaned.Trim();
    }

    // ==========================================
    // 供外部 (Copilot 中枢) 调用的气泡生成接口
    // ==========================================

    /// <summary> 生成工具结果的 AI 气泡，并可选择是否写入大模型记忆 </summary>
    public void AddSystemAIBubble(string text, bool remember = false)
    {
        text = FormatAIResponse(text);

        var aiMsg = new ChatMessageData { role = "assistant", content = text, timestamp = System.DateTime.Now.ToString() };
        _allMessages.Add(aiMsg);
        AddBubbleUI(aiMsg, false);

        // 【分级记忆核心】：如果要求记忆，手动写入大模型脑中
        if (remember && LLMManager.Instance != null)
        {
            LLMManager.Instance.AddToHistory("assistant", text);
        }
    }

    public class ChatBubbleTracker
    {
        public GameObject BubbleObject;
        public ChatMessageData MessageData;
    }

    /// <summary> 生成上下文引用气泡，并可选择是否写入大模型记忆 </summary>
    public ChatBubbleTracker AddContextQuoteBubble(string quoteText, bool remember = false)
    {
        string formattedQuote = $"<i><color=#555555>锚点：\n\"{quoteText}\"</color></i>";
        var quoteMsg = new ChatMessageData { role = "user", content = formattedQuote, timestamp = System.DateTime.Now.ToString() };
        _allMessages.Add(quoteMsg);

        // 【修改】：接收生成的物体对象
        GameObject obj = AddBubbleUI(quoteMsg, true);

        if (remember && LLMManager.Instance != null)
        {
            LLMManager.Instance.AddToHistory("user", $"我圈定了以下文本作为参考：\n{quoteText}");
        }

        // 返回追踪凭证
        return new ChatBubbleTracker { BubbleObject = obj, MessageData = quoteMsg };
    }

    // ==========================================
    // 气泡生成与销毁核心逻辑
    // ==========================================

    private GameObject AddBubbleUI(ChatMessageData msgData, bool isUser)
    {
        if (messagePrefab == null) return null;

        GameObject obj = Instantiate(messagePrefab, contentContainer);
        var ctrl = obj.GetComponent<ChatMessageController>();

        if (ctrl != null)
        {
            ctrl.Setup(msgData.content, isUser);

            if (ctrl.deleteButton != null)
            {
                ctrl.deleteButton.onClick.RemoveAllListeners();
                ctrl.deleteButton.onClick.AddListener(() => {
                    DeleteChatBubble(obj, msgData);
                });
            }
        }

        StartCoroutine(ScrollToBottom());
        return obj; // 返回生成的对象
    }

    public void RemoveSpecificBubble(ChatBubbleTracker tracker)
    {
        if (tracker == null || tracker.BubbleObject == null) return;
        DeleteChatBubble(tracker.BubbleObject, tracker.MessageData);
    }

    // 处理气泡销毁与三端数据同步
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

    private IEnumerator RebuildChatLayoutNextFrame()
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

    // ==========================================
    // 供外部调用的输入框金色引导光效 (AUBIM 4.0 视觉锚点)
    // ==========================================
    private Coroutine _inputGlowCoroutine;
    private Color _originalInputColor = Color.white; // 默认底色

    public void StartChatInputGlow()
    {
        if (chatInput == null) return;
        Image bg = chatInput.GetComponent<Image>();
        if (bg == null) return;

        if (_inputGlowCoroutine != null)
        {
            return; // 如果已经在闪烁，直接拦截，不重置颜色也不重新启动协程
        }

        _originalInputColor = bg.color;
        _inputGlowCoroutine = StartCoroutine(ChatInputGlowRoutine(bg));
    }

    public void StopChatInputGlow()
    {
        if (_inputGlowCoroutine != null)
        {
            StopCoroutine(_inputGlowCoroutine);
            _inputGlowCoroutine = null;
        }

        // 恢复原始颜色
        if (chatInput != null)
        {
            Image bg = chatInput.GetComponent<Image>();
            if (bg != null) bg.color = _originalInputColor;
        }
    }

    private IEnumerator ChatInputGlowRoutine(Image bg)
    {
        Color goldColor = new Color(1f, 0.84f, 0f, 1f); // 闪耀的金色
        float speed = 4f; // 呼吸频率，可自行微调
        while (true)
        {
            // 使用 Sin 函数制作平滑的呼吸灯效果
            float t = (Mathf.Sin(Time.time * speed) + 1f) / 2f;
            bg.color = Color.Lerp(_originalInputColor, goldColor, t);
            yield return null;
        }
    }

    // ==========================================
    // 气泡内容追加与独立生成接口 (Copilot 专用)
    // ==========================================

    /// <summary> 给已存在的引用气泡追加用户的附加指令 </summary>
    public void AppendTextToBubble(ChatBubbleTracker tracker, string extraText)
    {
        if (tracker == null || tracker.BubbleObject == null) return;

        // 1. 更新数据层，用醒目的颜色把用户的附加指令拼接到下面
        tracker.MessageData.content += $"\n\n<color=#00e5ff><b>[附加指令]：</b> {extraText}</color>";

        // 2. 更新 UI 层
        var ctrl = tracker.BubbleObject.GetComponent<ChatMessageController>();
        if (ctrl != null)
        {
            // 重新调用 Setup 刷新内部的 TextMeshPro 文本
            ctrl.Setup(tracker.MessageData.content, true);
        }

        // 3. 将追加了指令的完整内容写入大模型记忆中，确保 AI 能同时看到选区和要求
        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.AddToHistory("user", $"用户圈定了上述参考文本，并提出了具体要求：\n{extraText}");
        }
    }

    /// <summary> 纯手动创建一个普通的用户发言气泡 (不带引用框) </summary>
    public void AddStandardUserBubble(string displayText, string historyText = null, bool remember = true)
    {
        // 如果没有单独传历史文本，就默认用显示文本
        if (string.IsNullOrEmpty(historyText)) historyText = displayText;

        var msg = new ChatMessageData { role = "user", content = displayText, timestamp = System.DateTime.Now.ToString() };
        _allMessages.Add(msg);

        // 生成 UI
        AddBubbleUI(msg, true);

        // 存入记忆的文本是纯净的，不带富文本标签
        if (remember && LLMManager.Instance != null)
        {
            LLMManager.Instance.AddToHistory("user", historyText);
        }
    }
}