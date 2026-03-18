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

    private int _cachedSelectionStart = 0;
    private int _cachedSelectionEnd = 0;

    private bool _wasPromptFocused = false;
    private string _lastPreviewedText = "";
    private string _previewPlaceholder = "(请在上方输入您的润色指令并点击生成...)";

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
                _cachedSelectionStart = Mathf.Min(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
                _cachedSelectionEnd = Mathf.Max(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
            }

            // 【恢复焦点侦测】：找回提示预览功能
            bool isPromptFocused = (articlePromptInput != null && articlePromptInput.isFocused) ||
                                   (promptInput != null && promptInput.isFocused);

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

        int startIdx = _cachedSelectionStart;
        int endIdx = _cachedSelectionEnd;

        if (endIdx > startIdx)
        {
            string selectedText = mainBodyInput.text.Substring(startIdx, endIdx - startIdx);

            // 防抖：如果刚刚已经预览过了，就不重复追加
            if (selectedText == _lastPreviewedText) return;
            _lastPreviewedText = selectedText;

            string currentText = aiSuggestionInput.text;

            // 如果用户点了一次没生成，又去点了别的，把之前的提示改为“已取消”，保持历史干净
            if (currentText.Contains(_previewPlaceholder))
            {
                currentText = currentText.Replace(_previewPlaceholder, "(已取消润色，等待新指令)");
            }

            string divider = string.IsNullOrWhiteSpace(currentText) ? "" : "\n\n-------\n\n";
            aiSuggestionInput.text = currentText + divider + $"【已锁定待润色段落】\n\"{selectedText}\"\n\n{_previewPlaceholder}";

            StartCoroutine(ScrollToBottom(aiSuggestionInput));
        }
    }

    // ==========================================
    // 供外部访问的代理接口
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

        bool isHandlingProactiveIntervention = _promptController != null && !string.IsNullOrEmpty(_promptController.CurrentPromptInterventionType);

        if (isHandlingProactiveIntervention)
        {
            regenerateBtnText.text = "采纳建议";
            return;
        }

        // 使用安全的选区缓存来判断是否有选中文本
        bool hasTextSelection = (_cachedSelectionEnd > _cachedSelectionStart);
        bool hasNodeSelection = NodeCardManager.Instance != null && NodeCardManager.Instance.HasSelection();
        bool isBodyEmpty = mainBodyInput == null || string.IsNullOrWhiteSpace(mainBodyInput.text);

        if (hasTextSelection) regenerateBtnText.text = "局部润色";
        else if (hasNodeSelection) regenerateBtnText.text = "节点生成";
        else if (isBodyEmpty) regenerateBtnText.text = "全局生成";
        else regenerateBtnText.text = "全文优化";
    }

    public void OnRegenerateClicked()
    {
        bool isHandlingProactiveIntervention = _promptController != null && !string.IsNullOrEmpty(_promptController.CurrentPromptInterventionType);
        string previousInterventionType = "";

        if (isHandlingProactiveIntervention)
        {
            previousInterventionType = _promptController.CurrentPromptInterventionType;
            _promptController.SettlePromptIntention();

            if (UserBehaviorSystem.Instance != null)
                UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Adopt_AI, "ArticleModal", "ProactiveAccepted", 1);
        }

        string userInstruction = "";
        if (articlePromptInput != null && !string.IsNullOrWhiteSpace(articlePromptInput.text))
        {
            userInstruction = articlePromptInput.text;
            articlePromptInput.text = "";
        }
        else if (promptInput != null && !string.IsNullOrWhiteSpace(promptInput.text))
        {
            userInstruction = promptInput.text;
            promptInput.text = "";
        }

        if (mainBodyInput == null || aiSuggestionInput == null) return;

        // 读取安全的选区缓存，而不是去拿可能已经失焦清零的输入框状态
        int startIdx = _cachedSelectionStart;
        int endIdx = _cachedSelectionEnd;
        bool hasTextSelection = (endIdx > startIdx);

        bool hasNodeSelection = NodeCardManager.Instance != null && NodeCardManager.Instance.HasSelection();
        bool isBodyEmpty = string.IsNullOrWhiteSpace(mainBodyInput.text);

        // 路由 A：局部润色 (选中了正文)
        if (hasTextSelection)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Local, "ArticleModal", "LocalRefine", endIdx - startIdx);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请润色这段文字，使其更加流畅专业";

            string fullText = mainBodyInput.text;
            string selectedText = fullText.Substring(startIdx, endIdx - startIdx);
            int contextStart = Mathf.Max(0, startIdx - 500);
            int contextEnd = Mathf.Min(fullText.Length, endIdx + 500);

            // 执行后立刻清空选区缓存，防止下次误触发
            _cachedSelectionStart = 0;
            _cachedSelectionEnd = 0;
            _lastPreviewedText = "";
            StartCoroutine(HandleLocalRefinement(selectedText, fullText.Substring(contextStart, startIdx - contextStart), fullText.Substring(endIdx, contextEnd - endIdx), userInstruction));
        }

        // 路由 B：局部扩写 (选中了导图节点)
        else if (hasNodeSelection)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Node, "ArticleModal", "NodeGenerate", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请根据我提取的这些核心节点素材，详细扩写并连贯成文";
            StartCoroutine(HandleFullGeneration(userInstruction));
        }

        // 路由 C：从零开始 (正文是空的)
        else if (isBodyEmpty)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Global, "ArticleModal", "GlobalGenerate_FromScratch", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请根据全局导图的逻辑结构，从零开始写一篇结构严谨的文章";
            StartCoroutine(HandleFullGeneration(userInstruction));
        }

        // 路由 E：顺势续写 
        else if (previousInterventionType == InterventionType.ArticleGap.ToString() ||
                (!hasTextSelection && lastKnownCaretPosition >= mainBodyInput.text.Length - 10 && string.IsNullOrWhiteSpace(userInstruction)))
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Adopt_AI, "ArticleModal", "ContextualExpand", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请顺着前文的逻辑，自然地续写接下来的 1-2 个段落。";

            string draftText = mainBodyInput.text;
            int extractLength = Mathf.Min(draftText.Length, 300);
            string tailContext = draftText.Substring(draftText.Length - extractLength);

            StartCoroutine(HandleContextualExpansion(tailContext, userInstruction));
        }

        // 路由 D：宏观调整/全局审视
        else
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Global, "ArticleModal", "GlobalGenerate_RefineAll", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请审视当前全文的逻辑与连贯性，并给出重写或优化建议";

            string currentFullText = mainBodyInput.text;
            if (currentFullText.Length > 2000) currentFullText = currentFullText.Substring(0, 2000) + "\n...(后略)";

            string finalPrompt = $@"你是一个高级编辑。用户正在对整篇文章提出全局修改要求。
【用户的修改指令】：
{userInstruction}

【当前文章全文】(绝不要直接重复原文，只输出基于指令的修改结果或建议)：
{currentFullText}";

            string history = aiSuggestionInput.text;
            string divider = string.IsNullOrWhiteSpace(history) ? "" : "\n\n-------\n\n";
            aiSuggestionInput.text = history + divider + "[ AI 正在纵览全文并根据您的指令进行重构，请稍候... ]";
            StartCoroutine(ScrollToBottom(aiSuggestionInput));

            if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(true);

            if (LLMManager.Instance != null)
            {
                LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
                {
                    if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(false);

                    if (success)
                    {
                        string cleanRes = FormatChineseArticle(response);
                        aiSuggestionInput.text = history + divider + cleanRes;
                        if (InterventionTracker.Instance != null) InterventionTracker.Instance.GrantReadingBuffer(cleanRes.Length);
                    }
                    else aiSuggestionInput.text = history + divider + "[ 生成失败: " + response + " ]";

                    StartCoroutine(ScrollToBottom(aiSuggestionInput));
                });
            }
        }
    }

    // ==========================================
    // 大模型 LLM 请求协程 
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

        string history = aiSuggestionInput.text;
        string divider = string.IsNullOrWhiteSpace(history) ? "" : "\n\n-------\n\n";
        aiSuggestionInput.text = history + divider + "[  AI 正在撰写内容，请稍候... ]";
        StartCoroutine(ScrollToBottom(aiSuggestionInput));

        if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(true);

        if (LLMManager.Instance != null)
        {
            bool finished = false;
            LLMManager.Instance.TaskChat(fullPrompt, (response, success) =>
            {
                if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(false);

                if (success)
                {
                    aiSuggestionInput.text = history + divider + FormatChineseArticle(response);
                    if (InterventionTracker.Instance != null) InterventionTracker.Instance.GrantReadingBuffer(response.Length);
                }
                else aiSuggestionInput.text = history + divider + "[ 生成失败，请重试。\n错误信息: " + response + " ]";

                StartCoroutine(ScrollToBottom(aiSuggestionInput));
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

        string displayPrefix = $"【已锁定待润色段落】\n\"{selectedText}\"\n\n";
        string loadingText = "[ AI 正在思考并深度重构选中段落，请稍候... ]";

        // 【核心体验升级：无缝替换】
        string currentText = aiSuggestionInput.text;
        if (currentText.Contains(_previewPlaceholder))
        {
            // 如果刚才预览了占位符，直接把占位符替换成 Loading，丝般顺滑！
            aiSuggestionInput.text = currentText.Replace(_previewPlaceholder, loadingText);
        }
        else
        {
            // 如果用户手速极快没触发预览直接点生成，走标准追加流程
            string divider = string.IsNullOrWhiteSpace(currentText) ? "" : "\n\n-------\n\n";
            aiSuggestionInput.text = currentText + divider + displayPrefix + loadingText;
        }

        StartCoroutine(ScrollToBottom(aiSuggestionInput));

        if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(true);

        if (LLMManager.Instance != null)
        {
            bool finished = false;
            LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
            {
                if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(false);

                if (success)
                {
                    string cleanRes = FormatChineseArticle(response.Replace("处理后：", "").Replace("【选中文本】", "").Trim());
                    // 【精确替换 Loading，不破坏任何历史记录】
                    aiSuggestionInput.text = aiSuggestionInput.text.Replace(loadingText, $"【润色结果】\n{cleanRes}");
                    if (InterventionTracker.Instance != null) InterventionTracker.Instance.GrantReadingBuffer(cleanRes.Length);
                }
                else
                {
                    aiSuggestionInput.text = aiSuggestionInput.text.Replace(loadingText, $"[ 生成失败: {response} ]");
                }

                StartCoroutine(ScrollToBottom(aiSuggestionInput));
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

        string history = aiSuggestionInput.text;
        string divider = string.IsNullOrWhiteSpace(history) ? "" : "\n\n-------\n\n";
        aiSuggestionInput.text = history + divider + "[ AI 正在结合前文，为您构思接下来的内容... ]";
        StartCoroutine(ScrollToBottom(aiSuggestionInput));

        if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(true);

        if (LLMManager.Instance != null)
        {
            bool finished = false;
            LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
            {
                if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(false);

                if (success)
                {
                    string cleanRes = FormatChineseArticle(response.Replace("续写如下：", "").Trim());
                    aiSuggestionInput.text = history + divider + cleanRes;
                    if (InterventionTracker.Instance != null) InterventionTracker.Instance.GrantReadingBuffer(cleanRes.Length);
                }
                else aiSuggestionInput.text = history + divider + "[ 生成失败: " + response + " ]";

                StartCoroutine(ScrollToBottom(aiSuggestionInput));
                finished = true;
            });
            while (!finished) yield return null;
        }
    }

    // ==========================================
    // 基础 UI 与辅助功能 
    // ==========================================

    public void ExportToTxt()
    {
        if (mainBodyInput == null || string.IsNullOrWhiteSpace(mainBodyInput.text))
        {
            if (ToastSystem.Instance != null) ToastSystem.Instance.Show("正文区为空，没有可导出的内容！");
            return;
        }

        try
        {
            string targetFolder;
            if (ExperimentManager.Instance != null && !string.IsNullOrWhiteSpace(ExperimentManager.Instance.currentSubjectID))
            {
                string userRoot = ExperimentManager.GetUserFolderPath();
                targetFolder = Path.Combine(userRoot, "article");
            }
            else
            {
                targetFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AUBIM_文章导出");
            }

            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            string projectName = "未命名项目";
            if (SaveSystem.Instance != null && !string.IsNullOrWhiteSpace(SaveSystem.Instance.currentSaveName))
            {
                projectName = SaveSystem.Instance.currentSaveName;
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                projectName = projectName.Replace(c.ToString(), "");
            }

            string fileName = $"{projectName}.txt";
            string fullPath = Path.Combine(targetFolder, fileName);

            File.WriteAllText(fullPath, mainBodyInput.text, Encoding.UTF8);

            if (ToastSystem.Instance != null) ToastSystem.Instance.Show($"已保存至: {fileName}");
            Debug.Log($"<color=green>[导出成功]</color> 文章已覆盖归档至: {fullPath}");
        }
        catch (Exception e)
        {
            if (ToastSystem.Instance != null) ToastSystem.Instance.Show("导出失败，请查看控制台日志");
            Debug.LogError($"[导出失败] 无法导出 TXT 文件: {e.Message}");
        }
    }

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
                string formattedLine = line.Trim().StartsWith("#") ? cleanLine : "    " + cleanLine;
                sb.Append(formattedLine).Append("\n");
            }
        }
        return sb.ToString().Trim();
    }

    // ==========================================
    // 拖拽生成核心
    // ==========================================
    public void HandleNodeDropped(Vector2 screenPos, Camera cam, BaseNodeController node)
    {
        if (mainBodyInput == null || node == null || node.Data == null) return;

        TMP_Text textComp = mainBodyInput.textComponent;
        int insertIndex = TMP_TextUtilities.GetCursorIndexFromPosition(textComp, screenPos, cam);

        if (insertIndex == -1 || insertIndex > mainBodyInput.text.Length)
        {
            insertIndex = mainBodyInput.text.Length;
        }

        string nodeTitle = node.Data.Title;
        string nodeContent = node.Data.Content;

        StartCoroutine(GenerateParagraphFromDrop(insertIndex, nodeTitle, nodeContent, node.NodeID));
    }

    private IEnumerator GenerateParagraphFromDrop(int insertIndex, string title, string content, string nodeID)
    {
        string placeholderText = $"\n[ AI 正在将节点【{title}】展开为正文... ]\n";
        string originalText = mainBodyInput.text;
        mainBodyInput.text = originalText.Insert(insertIndex, placeholderText);

        string prompt = $@"你是一个学术写作助手。用户将思维导图的一个节点拖入了文章中。
请根据以下节点的标题和内容，扩写成一段自然流畅的正文段落，用于无缝插入到文章中。
【节点标题】：{title}
【节点内容】：{content}
【规则】：直接输出扩写后的段落，不要废话，不要输出标签，不要带有“输出如下”。";

        bool finished = false;
        string aiResult = "";

        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.TaskChat(prompt, (response, success) =>
            {
                if (success) aiResult = FormatChineseArticle(response);
                else aiResult = $"\n[ 节点展开失败: {response} ]\n";
                finished = true;
            });
        }
        else
        {
            aiResult = "LLM 管理器未初始化";
            finished = true;
        }

        while (!finished) yield return null;

        mainBodyInput.text = mainBodyInput.text.Replace(placeholderText, "\n" + aiResult + "\n");

        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Node, "Article", $"DragDropNode_{nodeID}", 1);
        }
    }

    // ==========================================
    // 拖拽悬停视觉反馈
    // ==========================================
    private RectTransform _dropCaret;

    public void UpdateDragDropFeedback(Vector2 screenPos, Camera cam)
    {
        if (mainBodyInput == null || !articleModal.activeSelf) return;

        if (_dropCaret == null)
        {
            GameObject caretObj = new GameObject("DropCaret_AUBIM");
            caretObj.transform.SetParent(mainBodyInput.textComponent.transform, false);
            Image img = caretObj.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 1f);

            _dropCaret = caretObj.GetComponent<RectTransform>();
            _dropCaret.pivot = new Vector2(0, 0);
            _dropCaret.sizeDelta = new Vector2(3f, mainBodyInput.textComponent.fontSize * 1.2f);
        }

        _dropCaret.gameObject.SetActive(true);

        TMP_Text textComp = mainBodyInput.textComponent;
        int insertIndex = TMP_TextUtilities.GetCursorIndexFromPosition(textComp, screenPos, cam);

        if (insertIndex == -1 || textComp.textInfo.characterCount == 0)
        {
            _dropCaret.localPosition = Vector3.zero;
            return;
        }

        insertIndex = Mathf.Clamp(insertIndex, 0, textComp.textInfo.characterCount);

        Vector3 caretPos = Vector3.zero;
        if (insertIndex < textComp.textInfo.characterCount)
        {
            caretPos = textComp.textInfo.characterInfo[insertIndex].bottomLeft;
        }
        else
        {
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

    // ==========================================
    // 视觉辅助：追加模式防弹级自动到底部 (智能自适应版)
    // ==========================================
    private IEnumerator ScrollToBottom(TMP_InputField inputField)
    {
        // 必须等待两帧！第一帧给文字赋值，第二帧给 UGUI 计算高度！
        yield return null;
        yield return null;

        if (inputField != null)
        {
            inputField.ForceLabelUpdate();
            Canvas.ForceUpdateCanvases();

            if (inputField.verticalScrollbar != null)
            {
                // 【核心修复】：判断内容是否超出了视口高度
                // size 代表滑块占整个轨道的比例。大于 0.99 说明内容很少，根本不需要滚动。
                if (inputField.verticalScrollbar.size < 0.99f)
                {
                    // 情况 A：长篇大论，内容超出一页了。执行追加模式，强压到最底部！(1f)
                    inputField.verticalScrollbar.value = 1f;
                }
                else
                {
                    // 情况 B：全新生成，内容很少。必须强行压到最顶部！(0f)
                    // 这样就能防止单行文字被死死拽到输入框的最下边。
                    inputField.verticalScrollbar.value = 0f;
                }
            }
        }
    }
}