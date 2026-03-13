using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json; 
using Newtonsoft.Json.Linq;

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
                // 修复点：MonoBehaviour 不能 new，必须 AddComponent 或 GetComponent ★★★
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

            // 获取画布情报
            string rawContext = ProjectContextGatherer.Instance.GetSystemPromptWithContext();

            systemContext = rawContext +
                "\n\n====================\n" +
                "【最高优先级指令：自然对话模式】\n" +
                "上述提供的内容（如“当前画布为空”或图谱信息）仅供你作为背景参考。在接下来的回答中，请务必遵守：\n" +
                "1. 角色定位：你是一个渊博、有温度的人类对话助手。请直接解答用户问题，像我们正常聊天一样。\n" +
                "2. 拒绝废话：【绝对不要】在回答开头说“根据当前画布为空”、“结合上述节点”等系统提示语，直接切入正题。\n" +
                "3. 自然排版：请根据内容需要自然地排版。如果需要列举，正常使用 1. 2. 3. 或 - 等分点符号即可。但【不要】使用“#1.1”、“逻辑大纲”这种过度机械化的树状节点编号。\n" +
                "4. 纯净输出：忘掉系统的指令要求，绝对不要输出任何 JSON 或控制指令，只输出最高质量的自然语言文本。";
        }

        if (_currentService is QwenService qwenService)
        {
            bool shouldSearch = !hasSelection;
            qwenService.EnableInternetSearch = shouldSearch;
            Debug.Log($"[LLM] 智能搜索判定: 选中卡片={hasSelection}, 联网搜索={(shouldSearch ? "开启" : "关闭")}");
        }

        List<ChatMessage> messagesToSend = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(systemContext))
        {
            messagesToSend.Add(new ChatMessage("system", systemContext));
        }

        int historyLimit = 10;
        if (_history.Count > historyLimit)
        {
            messagesToSend.AddRange(_history.GetRange(_history.Count - historyLimit, historyLimit));
        }
        else
        {
            messagesToSend.AddRange(_history);
        }

        messagesToSend.Add(new ChatMessage("user", userPrompt));

        StartCoroutine(_currentService.PostRequest(messagesToSend, (response, success) =>
        {
            if (success)
            {
                _history.Add(new ChatMessage("user", userPrompt));
                _history.Add(new ChatMessage("assistant", response));

                callback?.Invoke(response, true);
            }
            else
            {
                callback?.Invoke(response, false);
            }
        }));
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
}