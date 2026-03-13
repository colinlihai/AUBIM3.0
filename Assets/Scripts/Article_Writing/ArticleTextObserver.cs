using UnityEngine;
using TMPro;

/// <summary>
/// 负责监听主成文区的文本变化，精准判断用户的“复制粘贴”行为与隐性启发行为
/// </summary>
public class ArticleTextObserver : MonoBehaviour
{
    private string _lastMainBodyText = "";
    private ArticlePromptController _promptController;

    void Start()
    {
        _promptController = GetComponent<ArticlePromptController>();

        if (ArticleGenerator.Instance != null && ArticleGenerator.Instance.mainBodyInput != null)
        {
            _lastMainBodyText = ArticleGenerator.Instance.mainBodyInput.text;
            ArticleGenerator.Instance.mainBodyInput.onValueChanged.AddListener(OnMainBodyTextChanged);
        }
    }

    public void SyncHistoricalText(string text)
    {
        _lastMainBodyText = text ?? "";
    }

    private void OnMainBodyTextChanged(string newText)
    {
        if (newText == _lastMainBodyText) return;

        // 【隐性启发检测】
        if (_promptController != null && !string.IsNullOrEmpty(_promptController.CurrentPromptInterventionType))
        {
            string typeToLog = _promptController.CurrentPromptInterventionType;
            _promptController.ClearAndStopBreathing();
            Debug.Log($"<color=magenta>[AUBIM 亮点]</color> 用户受 Prompt 启发，切回主区打字！发送隐性采纳信号 (+0.5)！");

            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnImplicitScaffoldAccepted(typeToLog);
            }
        }

        int delta = newText.Length - _lastMainBodyText.Length;
        string pureNewText = System.Text.RegularExpressions.Regex.Replace(newText, @"\s+", "");
        string pureLastText = System.Text.RegularExpressions.Regex.Replace(_lastMainBodyText, @"\s+", "");

        string rawClipboard = GUIUtility.systemCopyBuffer ?? "";
        string pureClipboard = System.Text.RegularExpressions.Regex.Replace(rawClipboard, @"\s+", "");

        string rawAIText = (ArticleGenerator.Instance.aiSuggestionInput != null) ? ArticleGenerator.Instance.aiSuggestionInput.text : "";
        string pureAIText = System.Text.RegularExpressions.Regex.Replace(rawAIText, @"\s+", "");

        bool isAIPaste = false;
        bool isExternalPaste = false;

        if (pureClipboard.Length > 5)
        {
            int countInNew = CountSubstring(pureNewText, pureClipboard);
            int countInOld = CountSubstring(pureLastText, pureClipboard);

            if (countInNew > countInOld)
            {
                if (!string.IsNullOrEmpty(pureAIText) && pureAIText.Contains(pureClipboard)) isAIPaste = true;
                else isExternalPaste = true;
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

        _lastMainBodyText = newText;
    }

    private int CountSubstring(string text, string substring)
    {
        if (string.IsNullOrEmpty(substring)) return 0;
        int count = 0, index = 0;
        while ((index = text.IndexOf(substring, index, System.StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}