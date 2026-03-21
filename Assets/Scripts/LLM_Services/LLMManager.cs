using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json; 
using Newtonsoft.Json.Linq;
using System;

public enum LLMProvider
{
    Gemini,
    OpenAI,   // 预留
    DeepSeek, // 预留
    Local,     // 预留 (Ollama等)
    Qwen
}

public class LLMManager : MonoBehaviour
{
    public static LLMManager Instance;

    [Header("设置")]
    public LLMProvider currentProvider = LLMProvider.Qwen;
    public string apiKey = ""; // 注意：实际项目中不要直接上传到 Git

    // 当前正在使用的服务实现
    private ILLMService _currentService;
    // 对话历史记录 (只包含 User 和 Assistant，不包含动态的 System)
    private List<ChatMessage> _history = new List<ChatMessage>();
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        SwitchProvider(currentProvider);
    }

    public void SwitchProvider(LLMProvider provider)
    {
        currentProvider = provider;

        switch (provider)
        {
            case LLMProvider.Gemini:
                break;
            case LLMProvider.OpenAI:
                // _currentService = new OpenAIService(apiKey);
                Debug.LogWarning("OpenAI 尚未实现");
                break;
            case LLMProvider.Qwen:
                QwenService qwen = GetComponent<QwenService>();
                if (qwen == null)
                {
                    qwen = gameObject.AddComponent<QwenService>();
                }

                // 因为不能通过构造函数传参，所以手动调用 SetKey
                qwen.SetKey(apiKey);

                _currentService = qwen;
                break;
        }

        if (_currentService != null)
            _currentService.Init();

        Debug.Log($"[LLM] 已切换至 AI 提供商: {provider}");
    }

    public void ClearHistory()
    {
        _history.Clear();
        Debug.Log("[LLM] 对话历史已清空");
    }

    public void RemoveFromHistory(string targetContent)
    {
        // 倒序查找，防止内容重复时删错，优先删掉最近说的那句
        int index = _history.FindLastIndex(m => m.content == targetContent);
        if (index != -1)
        {
            _history.RemoveAt(index);
            Debug.Log($"[LLM] 成功从上下文中抹除了该条记忆。当前记忆数: {_history.Count}");
        }
    }

    // --- 供外部调用的统一入口 ---
    public void Chat(string userPrompt, LLMCallback callback)
    {
        if (_currentService == null)
        {
            callback?.Invoke("错误：AI服务未初始化", false);
            return;
        }

        bool hasSelection = false;
        string systemContext = "";

        if (ProjectContextGatherer.Instance != null)
        {
            if (NodeCardManager.Instance != null && NodeCardManager.Instance.GetSelectedNodes().Count > 0)
            {
                hasSelection = true;
            }

            string rawContext = ProjectContextGatherer.Instance.GetSystemPromptWithContext();

            systemContext = rawContext +
                "\n\n====================\n" +
                "【最高优先级指令：自然对话模式】\n" +
                "上述提供的内容仅供背景参考。在接下来的回答中，请务必遵守：\n" +
                "1. 角色定位：你是一个专业、利落的对话助手兼思维导师。\n" +
                "2. 去除 AI 格式（极度重要）：绝对禁止使用“这是一个好问题”、“首先/其次”、“总结一下”等常见 AI 套话。不要过度热情，直接给出核心信息。\n" +
                "3. 动态篇幅控制：对于【事实/科普类问题】（如天空为什么蓝），1-2段话讲清科学原理即可，绝对禁止注水；对于【开放/深度探讨类问题】（如如何开酒店），再进行详尽的逻辑展开。\n";
        }

        if (_currentService is QwenService qwenService)
        {
            bool shouldSearch = !hasSelection;
            qwenService.EnableInternetSearch = shouldSearch;
        }

        List<ChatMessage> messagesToSend = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemContext))
        {
            messagesToSend.Add(new ChatMessage("system", systemContext));
        }

        int historyLimit = 10;
        if (_history.Count > historyLimit)
            messagesToSend.AddRange(_history.GetRange(_history.Count - historyLimit, historyLimit));
        else
            messagesToSend.AddRange(_history);

        messagesToSend.Add(new ChatMessage("user", userPrompt));

        StartCoroutine(_currentService.PostRequest(messagesToSend, (response, success) =>
        {
            if (success)
            {
                // 1. 获取最纯净的长篇大论
                string pureAnswer = response.Trim();

                // 2. 正常加入历史记录，保证后续聊天的纯洁性
                _history.Add(new ChatMessage("user", userPrompt));
                _history.Add(new ChatMessage("assistant", pureAnswer));

                // 3. 瞬间回调给 UI 显示，用户立刻能看到大段回答
                callback?.Invoke(pureAnswer, true);

                // 【双轨黑科技】：后台隐蔽呼叫，专门去提取 3 个逼问！
                GenerateSocraticQuestionsInBackground(userPrompt, pureAnswer);
            }
            else
            {
                callback?.Invoke(response, false);
            }
        }));
    }

    // 专职生产逼问的后台流水线
    // 【修改 1】：改为 public，以便 AIChatManager 调用
    public void GenerateSocraticQuestionsInBackground(string userPrompt, string aiAnswer)
    {
        string truncatedAnswer = aiAnswer.Length > 1000 ? aiAnswer.Substring(0, 1000) + "..." : aiAnswer;

        // 去掉了容易让大模型格式错乱的 ===QUESTIONS=== 限制
        string taskPrompt = $@"你是一个极其犀利的思维引导师。
【用户提问】：{userPrompt}
【系统初步解答】：{truncatedAnswer}

请生成 3 个极其尖锐、反直觉或带有极端约束条件的“苏格拉底式发散提问”，用于激发用户的横向思维。
【绝对铁律】：
1. 只能且必须输出这 3 个问题，绝不要输出任何其他寒暄废话。
2. 每个问题严格按照“4字短标题|完整长句”的格式输出（注意中间的竖线|）。

【正确格式示例】（请严格模仿以下格式）：
0预算挑战|如果你的社团没有任何初始预算，你会用什么极端的方法开展第一次招新？
致命错误|请列举出三个最容易导致你这个社团在两个月内解散的致命错误？
跨界思考|如果用经营独立乐队的思维来重新审视人员管理，会有什么灵感？";

        // 调用后台纯净通道
        TaskChat(taskPrompt, (response, success) =>
        {
            if (success)
            {
                List<ChatSuggestionManager.SuggestionData> parsedSuggestions = new List<ChatSuggestionManager.SuggestionData>();

                // 【修改 2】：暴力按行解析，无视大模型的各种前缀和 Markdown
                string[] qLines = response.Split(new char[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in qLines)
                {
                    string cleanLine = line.Trim().Replace("*", "").Replace("-", "").Replace("#", "");
                    if (string.IsNullOrWhiteSpace(cleanLine) || cleanLine.Contains("QUESTION") || cleanLine.Contains("===") || cleanLine.Contains("```"))
                        continue;

                    // 剔除前缀如 "1. "
                    if (cleanLine.Length > 2 && char.IsDigit(cleanLine[0]) && (cleanLine[1] == '.' || cleanLine[1] == '、'))
                        cleanLine = cleanLine.Substring(2).Trim();

                    int splitPos = cleanLine.IndexOf('|');
                    if (splitPos == -1) splitPos = cleanLine.IndexOf('：');
                    if (splitPos == -1) splitPos = cleanLine.IndexOf(':');

                    if (splitPos != -1 && splitPos > 0 && splitPos < cleanLine.Length - 1)
                    {
                        parsedSuggestions.Add(new ChatSuggestionManager.SuggestionData
                        {
                            ShortTitle = cleanLine.Substring(0, splitPos).Trim(),
                            FullContent = cleanLine.Substring(splitPos + 1).Trim()
                        });
                    }
                }

                // 【修改 3】：增加解析失败的防静默死亡日志
                if (parsedSuggestions.Count > 0 && ChatSuggestionManager.Instance != null)
                {
                    Debug.Log($"<color=green>[LLM 逼问生成成功]</color> 解析到 {parsedSuggestions.Count} 条数据，交付给 Manager！");
                    ChatSuggestionManager.Instance.StartSuggestionLifecycle(aiAnswer.Length, parsedSuggestions);
                }
                else
                {
                    Debug.LogWarning($"<color=red>[LLM 逼问解析失败]</color> 未能解析出正确格式，原始返回：\n{response}");
                }
            }
        }, false, false);
    }

    /// <summary>
    /// 后台任务专属的纯净通道 (用于拟题、认知辅助、文章生成等)
    /// 绝对不注入 CMD 操作权限，绝对不拦截指令，绝对不污染聊天历史！
    /// </summary>
    /// <param name="taskPrompt">任务指令</param>
    /// <param name="callback">回调</param>
    /// <param name="injectTreeContext">是否需要让 AI 了解当前画布的全局树状结构？</param>
    public void TaskChat(string taskPrompt, LLMCallback callback, bool injectTreeContext = false, bool formatAsArticle = false)
    {
        if (_currentService == null)
        {
            callback?.Invoke("错误：AI服务未初始化", false);
            return;
        }

        if (_currentService is QwenService qwenService)
        {
            // 后台结构化任务建议关闭搜索，避免 AI 瞎联想导致格式破坏
            qwenService.EnableInternetSearch = false;
        }

        List<ChatMessage> messagesToSend = new List<ChatMessage>();

        // 1. 根据需要决定是否注入纯净的全局结构上下文 (不带任何 CMD 规则)
        if (injectTreeContext && ProjectContextGatherer.Instance != null)
        {
            string pureContext = ProjectContextGatherer.Instance.GetSystemPromptWithContext();
            messagesToSend.Add(new ChatMessage("system", pureContext));
        }

        // 2. 压入任务请求
        messagesToSend.Add(new ChatMessage("user", taskPrompt));

        // 3. 发送并纯净回调
        StartCoroutine(_currentService.PostRequest(messagesToSend, (response, success) =>
        {
            // 纯净返回，不做任何截断，也不往 _history 里存
            callback?.Invoke(response.Trim(), success);
        }));
    }

    /// <summary>
    /// 从存档恢复对话历史
    /// </summary>
    public void RestoreHistory(List<ChatMessageData> savedData)
    {
        _history.Clear();
        if (savedData == null) return;

        foreach (var item in savedData)
        {
            // 过滤掉 system 消息（通常存档里没有 system，但以防万一）
            if (item.role == "system") continue;

            _history.Add(new ChatMessage(item.role, item.content));
        }
        Debug.Log($"[LLM] 已恢复 {_history.Count} 条历史记忆");
    }

    // ==========================================
    // AUBIM 4.0: 显式记忆控制与动态上下文引擎
    // ==========================================

    /// <summary>
    /// 【新增】：供外部 (Copilot/UI) 手动将重要对话写入 AI 的永久记忆
    /// </summary>
    public void AddToHistory(string role, string content)
    {
        _history.Add(new ChatMessage(role, content));
    }

    /// <summary>
    /// 【新增】：支持挂载“动态上下文”的聊天方法
    /// 这里的 dynamicContext 作为 System Prompt 仅在本次请求发送，不会存入 _history 污染永久记忆！
    /// </summary>
    public void ChatWithDynamicContext(string userPrompt, string dynamicContext, Action<string, bool> callback)
    {
        if (_currentService == null)
        {
            callback?.Invoke("错误：AI服务未初始化", false);
            return;
        }

        // 1. 用户的提问存入永久记忆
        _history.Add(new ChatMessage("user", userPrompt));

        // 2. 组装本次发送的包裹
        List<ChatMessage> messagesToSend = new List<ChatMessage>();

        // 挂载动态上下文（黑科技：不在 _history 中，只发这一次！）
        if (!string.IsNullOrWhiteSpace(dynamicContext))
        {
            messagesToSend.Add(new ChatMessage("system", dynamicContext));
        }

        // 压入历史记忆
        messagesToSend.AddRange(_history);

        // 3. 发送请求
        StartCoroutine(_currentService.PostRequest(messagesToSend, (response, success) =>
        {
            // AI 的回答存入永久记忆
            if (success && !string.IsNullOrWhiteSpace(response))
            {
                _history.Add(new ChatMessage("assistant", response));
            }
            callback?.Invoke(response, success);
        }));
    }
}