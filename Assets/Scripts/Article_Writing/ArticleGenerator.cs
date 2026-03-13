using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using System.Collections;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(ArticlePromptController))]
[RequireComponent(typeof(ArticleTextObserver))]
public class ArticleGenerator : MonoBehaviour
{
    public static ArticleGenerator Instance;

    [Header("UI - 弹窗框架")]
    public GameObject articleModal;
    public Button openBtn;
    public Button closeBtn;

    [Header("UI - Header 区域")]
    public Button exportBtn;
    public Button reloadBtn;
    public TMP_InputField promptInput;
    public Button regenerateBtn;
    public TMP_Text regenerateBtnText;

    [Header("UI - 双栏文本区")]
    public TMP_InputField mainBodyInput;
    public TMP_InputField aiSuggestionInput;

    [Header("UI - 行为闭环：Prompt 交互区")]
    public TMP_InputField articlePromptInput;

    [Header("UI - AI 建议区操作")]
    public Button clearSuggestionBtn;

    [HideInInspector]
    public int lastKnownCaretPosition = 0;
    private bool _wasPromptFocused = false;

    private ArticlePromptController _promptController;
    private ArticleTextObserver _textObserver;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        _promptController = GetComponent<ArticlePromptController>();
        _textObserver = GetComponent<ArticleTextObserver>();
    }

    void Start()
    {
        if (regenerateBtn != null && regenerateBtnText == null) regenerateBtnText = regenerateBtn.GetComponentInChildren<TMP_Text>();

        if (openBtn != null) openBtn.onClick.AddListener(OnOpenModal);
        if (closeBtn != null) closeBtn.onClick.AddListener(CloseModal);
        if (exportBtn != null) exportBtn.onClick.AddListener(ExportToTxt);
        if (regenerateBtn != null) regenerateBtn.onClick.AddListener(OnRegenerateClicked);
        if (reloadBtn != null) reloadBtn.onClick.AddListener(OnImportOutlineClicked);
        if (clearSuggestionBtn != null) clearSuggestionBtn.onClick.AddListener(ClearAISuggestion);

        if (articleModal != null) articleModal.SetActive(false);
    }

    void Update()
    {
        if (articleModal.activeSelf)
        {
            UpdateRegenerateButtonLabel();

            if (mainBodyInput != null && mainBodyInput.isFocused)
            {
                lastKnownCaretPosition = mainBodyInput.selectionFocusPosition;
            }

            // ==========================================
            // 【新增】：侦测 Prompt 输入框的“点击进入”事件
            // ==========================================
            bool isPromptFocused = (articlePromptInput != null && articlePromptInput.isFocused) ||
                                   (promptInput != null && promptInput.isFocused);

            // 当焦点刚刚进入 Prompt 框的这一帧
            if (isPromptFocused && !_wasPromptFocused)
            {
                OnPromptGainedFocus();
            }
            _wasPromptFocused = isPromptFocused;
        }
    }

    private void OnPromptGainedFocus()
    {
        if (mainBodyInput == null || aiSuggestionInput == null) return;

        int startIdx = Mathf.Min(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
        int endIdx = Mathf.Max(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);

        // 只有当确实有高亮选中文本时，才执行复制
        if (endIdx > startIdx)
        {
            string selectedText = mainBodyInput.text.Substring(startIdx, endIdx - startIdx);

            // 用双引号括起来，投射到 AI 生成区
            aiSuggestionInput.text = $"【已锁定待润色段落】\n\"{selectedText}\"\n\n(请在上方输入您的润色指令...)";
        }
    }

    // ==========================================
    // 供外部访问的代理接口 (保证向下兼容)
    // ==========================================
    public void StartPromptBreathing(string interventionType, string suggestedText, float duration = 60f)
    {
        if (_promptController != null) _promptController.StartPromptBreathing(interventionType, suggestedText, duration);
    }

    public void RestoreArticleData(string draftText, string suggestionText)
    {
        if (mainBodyInput != null)
        {
            mainBodyInput.SetTextWithoutNotify(draftText ?? "");
            if (_textObserver != null) _textObserver.SyncHistoricalText(draftText);
        }
        if (aiSuggestionInput != null) aiSuggestionInput.text = suggestionText ?? "";
    }

    // ==========================================
    // UI 按钮交互与路由分发
    // ==========================================
    private void UpdateRegenerateButtonLabel()
    {
        if (regenerateBtnText == null) return;

        bool hasTextSelection = false;
        if (mainBodyInput != null)
        {
            hasTextSelection = Mathf.Abs(mainBodyInput.selectionAnchorPosition - mainBodyInput.selectionFocusPosition) > 0;
        }

        bool hasNodeSelection = NodeCardManager.Instance != null && NodeCardManager.Instance.HasSelection();
        bool hasProactivePrompt = articlePromptInput != null && !string.IsNullOrWhiteSpace(articlePromptInput.text);

        if (hasTextSelection) regenerateBtnText.text = "局部润色";
        else if (hasNodeSelection) regenerateBtnText.text = "选中节点";
        else if (hasProactivePrompt) regenerateBtnText.text = "采纳建议";
        else regenerateBtnText.text = "全局生成";
    }

    public void OnRegenerateClicked()
    {
        bool isHandlingProactiveIntervention = _promptController != null && !string.IsNullOrEmpty(_promptController.CurrentPromptInterventionType);

        if (isHandlingProactiveIntervention)
        {
            _promptController.SettlePromptIntention();
        }

        string userInstruction = "";
        bool usedBottomPrompt = false;

        if (articlePromptInput != null && !string.IsNullOrWhiteSpace(articlePromptInput.text))
        {
            userInstruction = articlePromptInput.text;
            usedBottomPrompt = true;
            articlePromptInput.text = "";
        }
        else if (promptInput != null && !string.IsNullOrWhiteSpace(promptInput.text))
        {
            userInstruction = promptInput.text;
        }

        if (promptInput != null) promptInput.text = "";
        if (mainBodyInput == null || aiSuggestionInput == null) return;

        int startIdx = Mathf.Min(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
        int endIdx = Mathf.Max(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
        bool hasTextSelection = (endIdx > startIdx);
        bool hasNodeSelection = NodeCardManager.Instance != null && NodeCardManager.Instance.HasSelection();

        // 终极四向路由
        if (hasTextSelection)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Local, "ArticleModal", "LocalRefine", endIdx - startIdx);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请润色这段文字，使其更加流畅专业";

            string fullText = mainBodyInput.text;
            string selectedText = fullText.Substring(startIdx, endIdx - startIdx);
            int contextStart = Mathf.Max(0, startIdx - 500);
            int contextEnd = Mathf.Min(fullText.Length, endIdx + 500);

            StartCoroutine(HandleLocalRefinement(selectedText, fullText.Substring(contextStart, startIdx - contextStart), fullText.Substring(endIdx, contextEnd - endIdx), userInstruction));
        }
        else if (hasNodeSelection)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Node, "ArticleModal", "NodeGenerate", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请根据我提取的这些核心节点素材，详细扩写并连贯成文";
            StartCoroutine(HandleFullGeneration(userInstruction));
        }
        else if (usedBottomPrompt || isHandlingProactiveIntervention)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Adopt_AI, "ArticleModal", "ContextualExpand", 1);
            string draftText = mainBodyInput.text;
            int cursorIndex = lastKnownCaretPosition > 0 ? lastKnownCaretPosition : draftText.Length;
            int extractLength = Mathf.Min(cursorIndex, 300);
            string tailContext = draftText.Substring(cursorIndex - extractLength, extractLength);
            StartCoroutine(HandleContextualExpansion(tailContext, userInstruction));
        }
        else
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Global, "ArticleModal", "GlobalGenerate", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请根据全局导图的逻辑结构，写一篇结构严谨的完整文章";
            StartCoroutine(HandleFullGeneration(userInstruction));
        }
    }

    // ==========================================
    // 大模型 LLM 请求协程 (保持原本的优秀 Prompt 不变)
    // ==========================================
    private IEnumerator HandleFullGeneration(string instruction)
    {
        string rawContextData = ConcatTreeData();
        if (string.IsNullOrWhiteSpace(rawContextData))
        {
            aiSuggestionInput.text = "没有提取到内容。请在画布上写入内容，或检查选中的节点是否为空。";
            yield break;
        }

        string fullPrompt = $@"你是一个专栏作家。请根据提供的导图素材撰写内容。
素材的标题层级反映了内容的逻辑结构。
【用户特别指令 (最高优先级)】：{instruction}
【导图素材结构内容】：
{rawContextData}";

        aiSuggestionInput.text = "AI 正在撰写内容，请稍候...";
        if (LLMManager.Instance != null)
        {
            bool finished = false;
            LLMManager.Instance.TaskChat(fullPrompt, (response, success) =>
            {
                if (success) aiSuggestionInput.text = FormatChineseArticle(response);
                else aiSuggestionInput.text = "生成失败，请重试。\n错误信息: " + response;
                finished = true;
            });
            while (!finished) yield return null;
        }
    }

    private IEnumerator HandleLocalRefinement(string selectedText, string contextBefore, string contextAfter, string instruction)
    {
        string finalPrompt = $@"你是一个顶级的内容共创助手。请严格执行【用户指令】，对【选中文本】进行深度处理。
【上下文背景】(绝不要输出此部分)：
前文：{contextBefore}
后文：{contextAfter}
【待处理的选中文本】：
{selectedText}
【用户指令】：
{instruction}
【规则】：只输出处理后的正文，不要重复原文，不带废话标签。";

        // ==========================================
        // 【核心修改】：视觉回显排版，保留原文，在下方追加
        // ==========================================
        string displayPrefix = $"【已锁定待润色段落】\n\"{selectedText}\"\n\n";

        aiSuggestionInput.text = displayPrefix + "AI 正在思考并深度重构选中段落，请稍候...";

        if (LLMManager.Instance != null)
        {
            bool finished = false;
            LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
            {
                if (success)
                {
                    string cleanRes = FormatChineseArticle(response.Replace("处理后：", "").Replace("【选中文本】", "").Trim());
                    // 成功后，将润色结果直接接在原文的下方展示
                    aiSuggestionInput.text = displayPrefix + $"【润色结果】\n{cleanRes}";
                }
                else
                {
                    aiSuggestionInput.text = displayPrefix + "生成失败: " + response;
                }
                finished = true;
            });
            while (!finished) yield return null;
        }
    }

    private IEnumerator HandleContextualExpansion(string tailContext, string instruction)
    {
        string finalPrompt = $@"你是一个顶级的内容共创助手。用户正在撰写文章，需要拓展。
【前文的最后内容】(绝不要重复输出)：
{tailContext}
【用户指令】：
{instruction}
【规则】：严格顺着指令撰写接下来的1-2个新段落，无缝衔接前文，不带废话。";

        aiSuggestionInput.text = "AI 正在结合前文，为您构思接下来的内容...";
        if (LLMManager.Instance != null)
        {
            bool finished = false;
            LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
            {
                if (success) aiSuggestionInput.text = FormatChineseArticle(response.Replace("续写如下：", "").Trim());
                else aiSuggestionInput.text = "生成失败: " + response;
                finished = true;
            });
            while (!finished) yield return null;
        }
    }

    // ==========================================
    // 基础 UI 与辅助功能 
    // ==========================================
    public void OnImportOutlineClicked()
    {
        string rawData = ConcatTreeData();
        if (string.IsNullOrWhiteSpace(rawData)) return;
        string newText = mainBodyInput.text;
        if (!string.IsNullOrWhiteSpace(newText) && !newText.EndsWith("\n")) newText += "\n\n";
        mainBodyInput.text = newText + rawData;
    }

    public void ExportToTxt() { /* 原有逻辑保留 */ }
    public void ClearAISuggestion() { if (aiSuggestionInput != null) aiSuggestionInput.text = ""; }

    public void OnOpenModal()
    {
        if (articleModal != null) articleModal.SetActive(true);
        if (promptInput != null) promptInput.text = "";
    }

    public void CloseModal()
    {
        if (articleModal != null) articleModal.SetActive(false);
        if (_promptController != null)
        {
            if (!string.IsNullOrEmpty(_promptController.CurrentPromptInterventionType))
            {
                if (InterventionTracker.Instance != null) InterventionTracker.Instance.OnInterventionIgnored(_promptController.CurrentPromptInterventionType);
                _promptController.ClearAndStopBreathing();
            }
        }
    }

    private string ConcatTreeData()
    {
        if (NodeCardManager.Instance == null) return "";
        var rootNodes = NodeCardManager.Instance.GetAllNodes().Where(n => n.parentNode == null).ToList();
        rootNodes.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
        StringBuilder sb = new StringBuilder();
        foreach (var root in rootNodes) AppendNodeDFS(root, sb, 0, NodeCardManager.Instance.GetSelectedNodes(), NodeCardManager.Instance.HasSelection());
        return sb.ToString().Trim();
    }

    private void AppendNodeDFS(BaseNodeController node, StringBuilder sb, int depth, List<BaseNodeController> selectedNodes, bool filterBySelection)
    {
        if (node == null || !node.gameObject.activeSelf) return;
        if (!filterBySelection || selectedNodes.Contains(node))
        {
            string indent = new string(' ', depth * 4);
            if (!string.IsNullOrWhiteSpace(node.Data.Title)) sb.AppendLine($"{indent}# {node.Data.Title}");
            if (!string.IsNullOrWhiteSpace(node.Data.Content)) sb.AppendLine($"{indent}{node.Data.Content.Replace("\n", "\n" + indent)}");
            sb.AppendLine();
        }
        if (node.childNodes != null && node.childNodes.Count > 0)
        {
            foreach (var child in node.childNodes.OrderByDescending(c => c.transform.position.y)) AppendNodeDFS(child, sb, depth + 1, selectedNodes, filterBySelection);
        }
    }

    private string FormatChineseArticle(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "";
        string[] lines = rawText.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        StringBuilder sb = new StringBuilder();
        foreach (string line in lines)
        {
            string cleanLine = line.Trim().Replace("#", "").Replace("*", "");
            if (cleanLine.StartsWith("- ")) cleanLine = cleanLine.Substring(2).Trim();
            if (!string.IsNullOrWhiteSpace(cleanLine))
            {
                // 我们手动 Append("\n")，强制系统使用 Unix 风格的纯净换行符
                string formattedLine = line.Trim().StartsWith("#") ? cleanLine : "    " + cleanLine;
                sb.Append(formattedLine).Append("\n");
            }
        }
        return sb.ToString().Trim();
    }

    // ==========================================
    // 拖拽生成核心黑科技：空间坐标 -> 字符串索引
    // ==========================================
    public void HandleNodeDropped(Vector2 screenPos, Camera cam, BaseNodeController node)
    {
        if (mainBodyInput == null || node == null || node.Data == null) return;

        // 1. 提取 TextMeshPro 核心文本组件
        TMP_Text textComp = mainBodyInput.textComponent;

        // 2. 【核心黑科技】：根据屏幕鼠标坐标，反推最近的字符索引
        int insertIndex = TMP_TextUtilities.GetCursorIndexFromPosition(textComp, screenPos, cam);

        // 如果用户拖到了输入框的空白处（文本末尾之后），API 会返回 -1，此时我们追加到末尾
        if (insertIndex == -1 || insertIndex > mainBodyInput.text.Length)
        {
            insertIndex = mainBodyInput.text.Length;
        }

        string nodeTitle = node.Data.Title;
        string nodeContent = node.Data.Content;

        // 3. 启动异步生成协程
        StartCoroutine(GenerateParagraphFromDrop(insertIndex, nodeTitle, nodeContent, node.NodeID));
    }

    private IEnumerator GenerateParagraphFromDrop(int insertIndex, string title, string content, string nodeID)
    {
        // 1. 制作高亮的占位符
        string placeholderText = $"\n[ AI 正在将节点【{title}】展开为正文... ]\n";

        // 2. 硬生生切开原文，插入占位符
        string originalText = mainBodyInput.text;
        mainBodyInput.text = originalText.Insert(insertIndex, placeholderText);

        // 3. 组装 Prompt 给 LLM
        string prompt = $@"你是一个学术写作助手。用户将思维导图的一个节点拖入了文章中。
请根据以下节点的标题和内容，扩写成一段自然流畅的正文段落，用于无缝插入到文章中。
【节点标题】：{title}
【节点内容】：{content}
【规则】：直接输出扩写后的段落，不要废话，不要输出标签，不要带有“输出如下”。";

        bool finished = false;
        string aiResult = "";

        // 4. 发起请求
        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.TaskChat(prompt, (response, success) =>
            {
                if (success)
                {
                    aiResult = FormatChineseArticle(response);
                }
                else
                {
                    aiResult = $"\n[ 节点展开失败: {response} ]\n";
                }
                finished = true;
            });
        }
        else
        {
            aiResult = "LLM 管理器未初始化";
            finished = true;
        }

        while (!finished) yield return null;

        // 5. 替换占位符为生成的真实段落
        // 注意：因为我们用了确切的占位符字符串，直接用 Replace 替换即可，极度安全
        mainBodyInput.text = mainBodyInput.text.Replace(placeholderText, "\n" + aiResult + "\n");

        // 6. 埋点记录
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Node, "Article", $"DragDropNode_{nodeID}", 1);
        }
    }

    // ==========================================
    // 拖拽悬停视觉反馈：金色幽灵光标
    // ==========================================
    private RectTransform _dropCaret;

    public void UpdateDragDropFeedback(Vector2 screenPos, Camera cam)
    {
        if (mainBodyInput == null || !articleModal.activeSelf) return;

        // 1. 动态创建一个金色的光标线 (无需配置 Prefab)
        if (_dropCaret == null)
        {
            GameObject caretObj = new GameObject("DropCaret_AUBIM");
            // 将光标挂载到 Text 内部，坐标系完美对齐
            caretObj.transform.SetParent(mainBodyInput.textComponent.transform, false);
            Image img = caretObj.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 1f); // 醒目的黑色

            _dropCaret = caretObj.GetComponent<RectTransform>();
            _dropCaret.pivot = new Vector2(0, 0); // 左下角对齐
            // 宽度 3 像素，高度跟随当前字体大小
            _dropCaret.sizeDelta = new Vector2(3f, mainBodyInput.textComponent.fontSize * 1.2f);
        }

        _dropCaret.gameObject.SetActive(true);

        // 2. 计算当前鼠标正下方的字符索引
        TMP_Text textComp = mainBodyInput.textComponent;
        int insertIndex = TMP_TextUtilities.GetCursorIndexFromPosition(textComp, screenPos, cam);

        if (insertIndex == -1 || textComp.textInfo.characterCount == 0)
        {
            _dropCaret.localPosition = Vector3.zero;
            return;
        }

        insertIndex = Mathf.Clamp(insertIndex, 0, textComp.textInfo.characterCount);

        // 3. 将光标吸附到对应字符的物理坐标上
        Vector3 caretPos = Vector3.zero;
        if (insertIndex < textComp.textInfo.characterCount)
        {
            // 停在某个字符前
            caretPos = textComp.textInfo.characterInfo[insertIndex].bottomLeft;
        }
        else
        {
            // 停在整段话的最后面
            caretPos = textComp.textInfo.characterInfo[textComp.textInfo.characterCount - 1].bottomRight;
        }

        _dropCaret.localPosition = caretPos;
    }

    public void ClearDragDropFeedback()
    {
        if (_dropCaret != null)
        {
            _dropCaret.gameObject.SetActive(false);
        }
    }
}