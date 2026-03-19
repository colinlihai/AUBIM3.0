using UnityEngine;
using TMPro;
using System;

/// <summary>
/// AUBIM 4.0 核心感知神经末梢
/// 负责监听主成文区的文本变化、光标游走和高亮选区，并向 Copilot 中枢进行实时广播
/// </summary>
public class ArticleTextObserver : MonoBehaviour
{
    // ==========================================
    // 全局状态广播频道 (Copilot 中枢会监听这些事件)
    // ==========================================

    /// <summary> 广播：选区状态变化 (是否有选区, 选中的文本内容) </summary>
    public static event Action<bool, string> OnSelectionChanged;

    /// <summary> 广播：光标位置变化 (光标是否在文本中间, 光标前的上下文, 光标后的上下文) </summary>
    public static event Action<bool, string, string> OnCaretContextChanged;

    /// <summary> 广播：文章字数变化 (当前字数) </summary>
    public static event Action<int> OnWordCountChanged;

    /// <summary> 广播：正文是否为空 (是否有任何文本) </summary>
    public static event Action<bool> OnTextEmptyStateChanged;

    // ==========================================

    private string _lastMainBodyText = "";

    // 状态缓存 (防抖机制，避免同一状态在 Update 中疯狂广播)
    private int _lastSelectionStart = -1;
    private int _lastSelectionEnd = -1;
    private int _lastCaretPos = -1;
    private int _lastWordCount = -1;
    private bool _lastHasText = false;

    void Start()
    {
        if (ArticleGenerator.Instance != null && ArticleGenerator.Instance.mainBodyInput != null)
        {
            _lastMainBodyText = ArticleGenerator.Instance.mainBodyInput.text;
            ArticleGenerator.Instance.mainBodyInput.onValueChanged.AddListener(OnMainBodyTextChanged);

            // 初始化广播一次当前状态
            CheckWordCountAndEmptyState(_lastMainBodyText);
        }
    }

    void Update()
    {
        var input = ArticleGenerator.Instance?.mainBodyInput;
        if (input == null || !input.isFocused) return;

        // 1. 侦测选区变化
        int currentStart = Mathf.Min(input.selectionAnchorPosition, input.selectionFocusPosition);
        int currentEnd = Mathf.Max(input.selectionAnchorPosition, input.selectionFocusPosition);

        if (currentStart != _lastSelectionStart || currentEnd != _lastSelectionEnd)
        {
            _lastSelectionStart = currentStart;
            _lastSelectionEnd = currentEnd;

            bool hasSelection = currentEnd > currentStart;
            string selectedText = hasSelection ? input.text.Substring(currentStart, currentEnd - currentStart) : "";

            // 广播给右侧侧边栏：选区变了！
            OnSelectionChanged?.Invoke(hasSelection, selectedText);
        }

        // 2. 侦测光标游走 (只有在没有选中文本时，光标游走才有意义)
        int currentCaretPos = input.caretPosition;
        if (currentStart == currentEnd && currentCaretPos != _lastCaretPos)
        {
            _lastCaretPos = currentCaretPos;
            BroadcastCaretContext(input.text, currentCaretPos);
        }
    }

    /// <summary>
    /// 处理并广播光标前后的上下文
    /// </summary>
    private void BroadcastCaretContext(string fullText, int caretPos)
    {
        if (string.IsNullOrEmpty(fullText))
        {
            OnCaretContextChanged?.Invoke(false, "", "");
            return;
        }

        caretPos = Mathf.Clamp(caretPos, 0, fullText.Length);

        string remainingText = fullText.Substring(caretPos);
        bool isMiddle = !string.IsNullOrWhiteSpace(remainingText);

        // 截取上下文引用 (前后各取最多 40 个字符，用于生成聊天区的 Quote Bubble)
        int beforeLen = Mathf.Min(40, caretPos);
        int afterLen = Mathf.Min(40, fullText.Length - caretPos);

        string beforeCtx = fullText.Substring(caretPos - beforeLen, beforeLen);
        string afterCtx = fullText.Substring(caretPos, afterLen);

        OnCaretContextChanged?.Invoke(isMiddle, beforeCtx, afterCtx);
    }

    private void OnMainBodyTextChanged(string newText)
    {
        if (newText == _lastMainBodyText) return;

        if (CopilotActionController.Instance != null)
        {
            CopilotActionController.Instance.HandleUserTyping();
        }

        CheckWordCountAndEmptyState(newText);

        // 保留 AUBIM 的核心遥测管道，记录删改行为
        TrackTelemetry(newText);

        _lastMainBodyText = newText;
    }

    private void CheckWordCountAndEmptyState(string text)
    {
        bool hasText = !string.IsNullOrWhiteSpace(text);
        if (hasText != _lastHasText)
        {
            _lastHasText = hasText;
            OnTextEmptyStateChanged?.Invoke(hasText);
        }

        // 计算当前有效字数 (排除了纯空格的回车)
        int wordCount = string.IsNullOrWhiteSpace(text) ? 0 : text.Replace(" ", "").Replace("\n", "").Length;
        if (wordCount != _lastWordCount)
        {
            _lastWordCount = wordCount;
            OnWordCountChanged?.Invoke(wordCount);
        }
    }

    // ==========================================
    // 遥测追踪逻辑 (继承自 3.0，保持数据科学管线的纯洁性)
    // ==========================================
    public void SyncHistoricalText(string text)
    {
        _lastMainBodyText = text ?? "";
    }

    private void TrackTelemetry(string newText)
    {
        int delta = newText.Length - _lastMainBodyText.Length;
        if (delta == 0) return;

        string clipboard = GUIUtility.systemCopyBuffer;
        string pureClipboard = clipboard?.Replace("\r", "").Replace("\n", "") ?? "";

        bool isExternalPaste = false;
        bool isAIPaste = false;

        // 侦测手动粘贴行为
        if (delta > 10 && !string.IsNullOrEmpty(pureClipboard))
        {
            int currentCount = CountSubstring(newText.Replace("\r", "").Replace("\n", ""), pureClipboard);
            int lastCount = CountSubstring(_lastMainBodyText.Replace("\r", "").Replace("\n", ""), pureClipboard);

            if (currentCount > lastCount)
            {
                // 暂时假定为外部粘贴，4.0 的 AI 注入不再走剪贴板，而是通过一键插入 API
                isExternalPaste = true;
            }
        }

        if (isAIPaste)
        {
            Debug.Log($"<color=green>[追踪]</color> 成功捕获 AI 建议采纳！有效字符数: {pureClipboard.Length}");
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Adopt_AI, "ArticleModal", "ManualPaste", pureClipboard.Length);
        }
        else if (isExternalPaste)
        {
            Debug.Log($"<color=orange>[追踪]</color> 捕获外部内容粘贴！有效字符数: {pureClipboard.Length}");
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Edit_Article_Body, "ArticleModal", "ExternalPaste", pureClipboard.Length);
        }
        else if (delta <= -5 && !isExternalPaste && !isAIPaste)
        {
            Debug.Log($"<color=red>[追踪]</color> 用户大段删除了文字！删除字数: {Mathf.Abs(delta)}");
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Delete_Text, "ArticleModal", "ManualDelete", Mathf.Abs(delta));
        }
    }

    private int CountSubstring(string text, string substring)
    {
        if (string.IsNullOrEmpty(substring)) return 0;
        int count = 0, i = 0;
        while ((i = text.IndexOf(substring, i, StringComparison.Ordinal)) != -1)
        {
            i += substring.Length;
            count++;
        }
        return count;
    }
}