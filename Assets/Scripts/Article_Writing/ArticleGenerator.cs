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
            // 侦测 Prompt 输入框的“点击进入”事件
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

        // ==========================================
        // 1. 最高优先级：判断当前是否有 AI 主动介入正在等待被采纳
        // ==========================================
        bool isHandlingProactiveIntervention = _promptController != null && !string.IsNullOrEmpty(_promptController.CurrentPromptInterventionType);

        if (isHandlingProactiveIntervention)
        {
            // 只有当真正的金色/灰色 AI 呼吸占位符存在时，才显示“采纳建议”
            regenerateBtnText.text = "采纳建议";
            return;
        }

        // ==========================================
        // 2. 如果没有主动介入，则进入手动模式的四向路由 UI 匹配
        // ==========================================
        bool hasTextSelection = false;
        if (mainBodyInput != null)
        {
            hasTextSelection = Mathf.Abs(mainBodyInput.selectionAnchorPosition - mainBodyInput.selectionFocusPosition) > 0;
        }

        bool hasNodeSelection = NodeCardManager.Instance != null && NodeCardManager.Instance.HasSelection();
        bool isBodyEmpty = mainBodyInput == null || string.IsNullOrWhiteSpace(mainBodyInput.text);

        // 严格对应我们的路由逻辑 A, B, C, D
        if (hasTextSelection)
        {
            regenerateBtnText.text = "局部润色";    // 路由 A：选中了正文
        }
        else if (hasNodeSelection)
        {
            regenerateBtnText.text = "节点生成";    // 路由 B：选中了思维导图节点
        }
        else if (isBodyEmpty)
        {
            regenerateBtnText.text = "全局生成";    // 路由 C：正文为空，结合全节点从零写起
        }
        else
        {
            regenerateBtnText.text = "全文优化";    // 路由 D：正文有字，没选正文也没选节点，针对全文提要求
        }
    }

    public void OnRegenerateClicked()
    {
        // 1. 判断当前是否是系统在闪烁金光（等待用户采纳发呆建议）
        bool isHandlingProactiveIntervention = _promptController != null && !string.IsNullOrEmpty(_promptController.CurrentPromptInterventionType);

        if (isHandlingProactiveIntervention)
        {
            // 如果是，结算这次 AI 介入，直接走之前的占位符上屏逻辑
            _promptController.SettlePromptIntention();
            if (UserBehaviorSystem.Instance != null)
                UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Adopt_AI, "ArticleModal", "ProactiveAccepted", 1);

            return; // 核心修改：如果是采纳系统主动发呆建议，直接 return 结束！不再走后面的生成逻辑！
        }

        // 2. 如果不是采纳建议，那就是真正的“手动发起请求”
        string userInstruction = "";

        if (articlePromptInput != null && !string.IsNullOrWhiteSpace(articlePromptInput.text))
        {
            userInstruction = articlePromptInput.text;
            articlePromptInput.text = ""; // 清空输入框
        }
        else if (promptInput != null && !string.IsNullOrWhiteSpace(promptInput.text))
        {
            userInstruction = promptInput.text;
            promptInput.text = "";
        }

        if (mainBodyInput == null || aiSuggestionInput == null) return;

        // 获取用户当前的选中状态
        int startIdx = Mathf.Min(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
        int endIdx = Mathf.Max(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
        bool hasTextSelection = (endIdx > startIdx);
        bool hasNodeSelection = NodeCardManager.Instance != null && NodeCardManager.Instance.HasSelection();
        bool isBodyEmpty = string.IsNullOrWhiteSpace(mainBodyInput.text);

        // ==========================================
        // 终极精准路由树：区分用户手动输入的 4 种意图
        // ==========================================

        // 路由 A：局部润色 (选中了正文)
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

        // 路由 B：局部扩写 (没选正文，但选中了导图节点)
        else if (hasNodeSelection)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Node, "ArticleModal", "NodeGenerate", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请根据我提取的这些核心节点素材，详细扩写并连贯成文";

            // 注意：此时 ConcatTreeData 内部会自动根据 HasSelection() 过滤，只拼装选中的节点
            StartCoroutine(HandleFullGeneration(userInstruction));
        }

        // 路由 C：从零开始 (正文是空的，且没选任何东西)
        else if (isBodyEmpty)
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Global, "ArticleModal", "GlobalGenerate_FromScratch", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请根据全局导图的逻辑结构，从零开始写一篇结构严谨的文章";

            // 全局节点 + 用户指令 -> 生成初稿
            StartCoroutine(HandleFullGeneration(userInstruction));
        }

        // 路由 D：宏观调整/全局审视 (正文有字，没选正文也没选节点)
        // 修复：这里以前被错误地当成了“续写”，现在修正为针对全篇的建议或重写
        else
        {
            if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Global, "ArticleModal", "GlobalGenerate_RefineAll", 1);
            if (string.IsNullOrWhiteSpace(userInstruction)) userInstruction = "请审视当前全文的逻辑与连贯性，并给出重写或优化建议";

            string currentFullText = mainBodyInput.text;
            // 因为字数可能很多，我们需要截取防爆（大模型一次吃不下太多）
            if (currentFullText.Length > 2000) currentFullText = currentFullText.Substring(0, 2000) + "\n...(后略)";

            // 专门写一个新的 Prompt 调用，融合 全文 + 导图大纲 + 用户指令
            string finalPrompt = $@"你是一个高级编辑。用户正在对整篇文章提出全局修改要求。
【用户的全局修改指令】：
{userInstruction}

【当前文章全文】(绝不要直接重复原文，只输出基于指令的修改结果或建议)：
{currentFullText}";

            aiSuggestionInput.text = "AI 正在纵览全文并根据您的指令进行重构，请稍候...";

            if (LLMManager.Instance != null)
            {
                LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
                {
                    if (success) aiSuggestionInput.text = FormatChineseArticle(response);
                    else aiSuggestionInput.text = "生成失败: " + response;
                });
            }
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
        mainBodyInput.text = (newText + rawData).Replace("\r", "");
    }

    public void ExportToTxt()
    {
        if (mainBodyInput == null || string.IsNullOrWhiteSpace(mainBodyInput.text))
        {
            if (ToastSystem.Instance != null) ToastSystem.Instance.Show("正文区为空，没有可导出的内容！");
            return;
        }

        try
        {
            // 1. 获取目标文件夹路径
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

            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            // 2. 获取当前项目名称作为文件名
            string projectName = "未命名项目";
            if (SaveSystem.Instance != null && !string.IsNullOrWhiteSpace(SaveSystem.Instance.currentSaveName))
            {
                projectName = SaveSystem.Instance.currentSaveName;
            }

            // 【防爆设计】：过滤掉项目名中可能包含的非法路径字符 (如 \ / : * ? " < > |)
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                projectName = projectName.Replace(c.ToString(), "");
            }

            string fileName = $"{projectName}.txt";
            string fullPath = Path.Combine(targetFolder, fileName);

            // 3. 将正文内容以 UTF-8 编码写入 TXT 文件
            // 注：File.WriteAllText 默认行为就是“如果文件存在，则完全覆盖它”，完美契合需求
            File.WriteAllText(fullPath, mainBodyInput.text, Encoding.UTF8);

            // 4. 视觉与日志反馈
            if (ToastSystem.Instance != null)
                ToastSystem.Instance.Show($"已保存至: {fileName}");

            Debug.Log($"<color=green>[导出成功]</color> 文章已覆盖归档至: {fullPath}");
        }
        catch (Exception e)
        {
            if (ToastSystem.Instance != null)
                ToastSystem.Instance.Show("导出失败，请查看控制台日志");

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