using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// AUBIM 4.0 统一副驾驶中枢 (Unified Copilot Brain) - 终极完全体
/// 接管 5 个生成按钮，监听成文区状态，处理 AI 主动介入、全局导图读取与拖拽生成
/// </summary>
public class CopilotActionController : MonoBehaviour
{
    public static CopilotActionController Instance;

    [Header("Copilot 工具按钮 (请拖入对应的 Button)")]
    public GameObject btnGlobalDraft;       // 全文起草 (常驻)
    public GameObject btnLocalRefine;       // 局部润色
    public GameObject btnContextExpand;     // 顺势续写 (光标在段尾)
    public GameObject btnContextTransition; // 内容衔接 (光标在中间)
    public GameObject btnGlobalReview;      // 审稿意见 (字数>800)

    // 状态缓存
    private bool _hasSelection = false;
    private string _selectedText = "";
    private bool _isCaretInMiddle = false;
    private string _contextBefore = "";
    private string _contextAfter = "";
    private bool _hasText = false;
    private int _wordCount = 0;

    private GameObject _currentlyGlowingBtn = null;

    private string _lastQuotedText = ""; // 防抖：记录上次生成的气泡内容

    private bool _isModalOpen = false;

    public enum ToolMode { None, ExpandWaiting, TransitionWaiting, RefineWaiting, ReviewWaiting, DraftWaiting }
    private ToolMode _currentMode = ToolMode.None;

    // 缓存按钮的原始颜色，用于复原
    private Color _btnOriginalColor = Color.white;
    private Color _colorYellow = new Color(1f, 0.8f, 0.2f, 1f); // 待命黄
    private Color _colorGreen = new Color(0.3f, 0.8f, 0.3f, 1f);  // 执行绿

    private Coroutine _quoteDebounceCoroutine;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        // 订阅 ArticleTextObserver 的情报广播
        ArticleTextObserver.OnSelectionChanged += HandleSelectionChanged;
        ArticleTextObserver.OnCaretContextChanged += HandleCaretContextChanged;
        ArticleTextObserver.OnWordCountChanged += HandleWordCountChanged;
        ArticleTextObserver.OnTextEmptyStateChanged += HandleTextEmptyStateChanged;

        // 绑定 5 个按钮的点击事件
        if (btnGlobalDraft != null) btnGlobalDraft.GetComponent<Button>().onClick.AddListener(OnGlobalDraftClicked);
        if (btnLocalRefine != null) btnLocalRefine.GetComponent<Button>().onClick.AddListener(OnLocalRefineClicked);
        if (btnContextExpand != null) btnContextExpand.GetComponent<Button>().onClick.AddListener(OnContextExpandClicked);
        if (btnContextTransition != null) btnContextTransition.GetComponent<Button>().onClick.AddListener(OnContextTransitionClicked);
        if (btnGlobalReview != null) btnGlobalReview.GetComponent<Button>().onClick.AddListener(OnGlobalReviewClicked);

        if (btnContextExpand != null) _btnOriginalColor = btnContextExpand.GetComponent<Image>().color;

        UpdateButtonVisibility();
    }

    void Start()
    {
        // 【拼图三】：订阅 ArticleGenerator 的拖拽生成事件
        if (ArticleGenerator.Instance != null)
        {
            ArticleGenerator.Instance.OnNodeDroppedEvent += HandleNodeDroppedEvent;
        }
    }

    void Update()
    {
        if (ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal != null)
        {
            bool currentModalState = ArticleGenerator.Instance.articleModal.activeSelf;
            if (currentModalState != _isModalOpen)
            {
                _isModalOpen = currentModalState;
                UpdateButtonVisibility(); // 当成文区打开/关闭时，立刻刷新按钮显示！

                if (!_isModalOpen) ResetToolMode();
            }
        }
    }

    void OnDisable()
    {
        ArticleTextObserver.OnSelectionChanged -= HandleSelectionChanged;
        ArticleTextObserver.OnCaretContextChanged -= HandleCaretContextChanged;
        ArticleTextObserver.OnWordCountChanged -= HandleWordCountChanged;
        ArticleTextObserver.OnTextEmptyStateChanged -= HandleTextEmptyStateChanged;

        if (ArticleGenerator.Instance != null)
        {
            ArticleGenerator.Instance.OnNodeDroppedEvent -= HandleNodeDroppedEvent;
        }
    }

    // ==========================================
    // 监听成文区广播 -> 更新按钮显隐状态
    // ==========================================
    private void HandleSelectionChanged(bool hasSel, string selText)
    {
        _hasSelection = hasSel;
        _selectedText = selText;
        UpdateButtonVisibility();

        // 选中文字时，也延迟 0.6 秒自动出气泡
        if (hasSel && _currentMode == ToolMode.RefineWaiting)
        {
            if (_quoteDebounceCoroutine != null) StopCoroutine(_quoteDebounceCoroutine);
            _quoteDebounceCoroutine = StartCoroutine(DebounceQuoteBubble());
        }
    }

    private void HandleCaretContextChanged(bool isMiddle, string before, string after)
    {
        _isCaretInMiddle = isMiddle;
        _contextBefore = before;
        _contextAfter = after;
        UpdateButtonVisibility();

        // 【核心修改】：在待命状态下，光标移动时触发防抖气泡
        if (_currentMode == ToolMode.ExpandWaiting || _currentMode == ToolMode.TransitionWaiting)
        {
            if (_quoteDebounceCoroutine != null) StopCoroutine(_quoteDebounceCoroutine);
            _quoteDebounceCoroutine = StartCoroutine(DebounceQuoteBubble());
        }
    }

    private void HandleWordCountChanged(int count)
    {
        _wordCount = count;
        UpdateButtonVisibility();
    }

    private void HandleTextEmptyStateChanged(bool hasText)
    {
        _hasText = hasText;
        UpdateButtonVisibility();
    }

    private void UpdateButtonVisibility()
    {
        // 【核心修复】：如果成文区根本没打开，强制隐藏所有副驾驶按钮，保持聊天区清爽！
        if (!_isModalOpen)
        {
            if (btnGlobalDraft != null) btnGlobalDraft.SetActive(false);
            if (btnLocalRefine != null) btnLocalRefine.SetActive(false);
            if (btnContextExpand != null) btnContextExpand.SetActive(false);
            if (btnContextTransition != null) btnContextTransition.SetActive(false);
            if (btnGlobalReview != null) btnGlobalReview.SetActive(false);
            return;
        }

        if (btnGlobalDraft != null) btnGlobalDraft.SetActive(true);
        if (btnLocalRefine != null) btnLocalRefine.SetActive(_hasText);
        if (btnContextExpand != null) btnContextExpand.SetActive(_hasText);
        if (btnContextTransition != null) btnContextTransition.SetActive(_hasText);

        if (btnGlobalReview != null) btnGlobalReview.SetActive(_hasText && _wordCount > 500);
    }

    private IEnumerator DebounceQuoteBubble()
    {
        yield return new WaitForSeconds(0.6f); // 停顿 0.6 秒，确认用户找准了位置
        TryGenerateQuoteBubble();
    }

    public void OnChatInputSelected()
    {
        if (AIChatManager.Instance != null) AIChatManager.Instance.StopChatInputGlow();

        // 如果用户等不及 0.6 秒，直接点击了输入框，立刻中断倒计时并出气泡！
        if (_quoteDebounceCoroutine != null) StopCoroutine(_quoteDebounceCoroutine);
        TryGenerateQuoteBubble();
    }

    public void TryGenerateQuoteBubble()
    {
        if (!_isModalOpen) return;

        string quoteToGenerate = "";

        if (_hasSelection && _currentMode == ToolMode.RefineWaiting && !string.IsNullOrWhiteSpace(_selectedText))
        {
            quoteToGenerate = $"你要润色的内容为：\n\"{_selectedText}\"";
        }
        else if (_hasText && _currentMode == ToolMode.ExpandWaiting)
        {
            // 【核心修改】：顺势续写不再限制位置，无论光标在哪，永远只取上文作为续写基石
            quoteToGenerate = $"你要续写的前文为：\n...{_contextBefore}";
        }
        else if (_hasText && _currentMode == ToolMode.TransitionWaiting)
        {
            // 【核心修改】：内容衔接最好在中间。如果用户非要在末尾点内容衔接，依然给出兼容提示
            if (_isCaretInMiddle)
                quoteToGenerate = $"你要关联的上下文为：\n...{_contextBefore} | {_contextAfter}...";
            else
                quoteToGenerate = $"你要关联的上下文为：\n...{_contextBefore} | [文章末尾无下文]";
        }

        if (string.IsNullOrWhiteSpace(quoteToGenerate) || quoteToGenerate == _lastQuotedText) return;

        _lastQuotedText = quoteToGenerate;

        if (AIChatManager.Instance != null)
        {
            AIChatManager.Instance.AddContextQuoteBubble(quoteToGenerate, true);

            if (_currentMode != ToolMode.None)
            {
                AIChatManager.Instance.StartChatInputGlow();
            }
        }
    }

    // ==========================================
    // 状态机管理 & 拦截回车
    // ==========================================
    private void ResetToolMode()
    {
        _currentMode = ToolMode.None;
        _lastQuotedText = ""; // 允许下次重新生成气泡

        if (AIChatManager.Instance != null) AIChatManager.Instance.StopChatInputGlow();

        if (btnLocalRefine != null)
        {
            btnLocalRefine.GetComponent<Image>().color = _btnOriginalColor;
            btnLocalRefine.GetComponentInChildren<TMP_Text>().text = "局部润色";
        }
        if (btnContextExpand != null)
        {
            btnContextExpand.GetComponent<Image>().color = _btnOriginalColor;
            btnContextExpand.GetComponentInChildren<TMP_Text>().text = "顺势续写";
        }
        if (btnContextTransition != null)
        {
            btnContextTransition.GetComponent<Image>().color = _btnOriginalColor;
            btnContextTransition.GetComponentInChildren<TMP_Text>().text = "内容衔接";
        }
        if (btnGlobalReview != null)
        {
            btnGlobalReview.GetComponent<Image>().color = _btnOriginalColor;
            btnGlobalReview.GetComponentInChildren<TMP_Text>().text = "审稿意见";
        }
        if (btnGlobalDraft != null)
        {
            btnGlobalDraft.GetComponent<Image>().color = _btnOriginalColor;
            btnGlobalDraft.GetComponentInChildren<TMP_Text>().text = "全文起草";
        }
    }

    /// <summary>
    /// 当聊天区按下回车时，AIChatManager 会调用此方法
    /// </summary>
    public bool TryInterceptChatSubmit(string userPrompt)
    {
        if (_currentMode == ToolMode.RefineWaiting)
        {
            ExecuteRefine(userPrompt);
            return true;
        }
        else if (_currentMode == ToolMode.ExpandWaiting)
        {
            ExecuteExpand(userPrompt);
            return true;
        }
        else if (_currentMode == ToolMode.TransitionWaiting)
        {
            ExecuteTransition(userPrompt);
            return true;
        }
        else if (_currentMode == ToolMode.ReviewWaiting)
        {
            ExecuteReview(userPrompt);
            return true;
        }
        else if (_currentMode == ToolMode.DraftWaiting)
        { 
            ExecuteDraft(userPrompt); return true; 
        }
        return false; // 没拦截，正常聊天
    }

    // ==========================================
    // 实际执行逻辑 (由回车触发，变绿)
    // ==========================================
    private void ExecuteExpand(string userPrompt)
    {
        string requirement = string.IsNullOrWhiteSpace(userPrompt) ? "请自然地续写接下来的 1-2 个段落。" : userPrompt;
        string prompt = $"请根据要求：【{requirement}】，紧接在以下文本之后续写新内容，直接输出结果：\n{_contextBefore}";

        btnContextExpand.GetComponent<Image>().color = _colorGreen;
        btnContextExpand.GetComponentInChildren<TMP_Text>().text = "正在续写...";

        ExecuteToolPrompt(prompt, "ContextExpand", true, userPrompt, (success, response) =>
        {
            ResetToolMode();
            if (AIChatManager.Instance != null)
                AIChatManager.Instance.AddSystemAIBubble(success ? response : $"[执行失败: {response}]", true);
        });
    }

    private void ExecuteRefine(string userPrompt)
    {
        // 防御性拦截：如果没选中文字就按了回车
        if (!_hasSelection || string.IsNullOrWhiteSpace(_selectedText))
        {
            if (AIChatManager.Instance != null) AIChatManager.Instance.AddSystemAIBubble("[执行失败: 未检测到选中的文本，请先在右侧高亮选择要润色的内容。]");
            ResetToolMode();
            return;
        }

        string requirement = string.IsNullOrWhiteSpace(userPrompt) ? "请润色这段文字，使其更加流畅专业。" : userPrompt;
        string prompt = $"你是一个资深编辑。请根据要求：【{requirement}】，对以下文本进行润色重写，直接输出结果，不要解释：\n{_selectedText}";

        btnLocalRefine.GetComponent<Image>().color = _colorGreen;
        btnLocalRefine.GetComponentInChildren<TMP_Text>().text = "正在润色...";

        ExecuteToolPrompt(prompt, "LocalRefine", true, userPrompt, (success, response) =>
        {
            ResetToolMode();
            if (AIChatManager.Instance != null)
                AIChatManager.Instance.AddSystemAIBubble(success ? response : $"[执行失败: {response}]", true);
        });
    }

    private void ExecuteTransition(string userPrompt)
    {
        string requirement = string.IsNullOrWhiteSpace(userPrompt) ? "请生成一段过渡文字，使上下文逻辑连贯。" : userPrompt;
        string prompt = $"请根据要求：【{requirement}】，在以下两段文本之间生成过渡内容。直接输出这部分过渡段落。\n上文：{_contextBefore}\n下文：{_contextAfter}";

        btnContextTransition.GetComponent<Image>().color = _colorGreen;
        btnContextTransition.GetComponentInChildren<TMP_Text>().text = "正在衔接...";

        ExecuteToolPrompt(prompt, "ContextTransition", true, userPrompt, (success, response) =>
        {
            ResetToolMode();
            if (AIChatManager.Instance != null)
                AIChatManager.Instance.AddSystemAIBubble(success ? response : $"[执行失败: {response}]", true);
        });
    }

    private void ExecuteReview(string userPrompt)
    {
        string requirement = string.IsNullOrWhiteSpace(userPrompt) ? "请给出结构和逻辑上的修改建议。" : userPrompt;
        string fullText = ArticleGenerator.Instance.mainBodyInput.text;
        string prompt = $"作为学术审稿人，请阅读以下全文。根据侧重点：【{requirement}】，给出逻辑和结构上的修改建议，切勿直接重写正文：\n{fullText}";

        btnGlobalReview.GetComponent<Image>().color = _colorGreen;
        btnGlobalReview.GetComponentInChildren<TMP_Text>().text = "正在审阅...";

        // 注意：重量级功能，传 false 不记忆大段内容，避免 Token 爆炸
        ExecuteToolPrompt(prompt, "GlobalReview", false, "", (success, response) =>
        {
            ResetToolMode();
            if (AIChatManager.Instance != null)
                AIChatManager.Instance.AddSystemAIBubble(success ? response : $"[执行失败: {response}]", false);
        });
    }

    private void ExecuteDraft(string userPrompt)
    {
        string rawContextData = ConcatTreeData();
        if (string.IsNullOrWhiteSpace(rawContextData))
        {
            if (AIChatManager.Instance != null) AIChatManager.Instance.AddSystemAIBubble("[ 提取失败：当前导图没有提取到任何有效节点内容。]");
            ResetToolMode();
            return;
        }

        string requirement = string.IsNullOrWhiteSpace(userPrompt) ? "请根据全局导图的逻辑结构，从零开始写一篇结构严谨的文章。" : userPrompt;
        string prompt = $@"你是一个专栏作家。请根据提供的导图素材撰写内容。
素材的标题层级反映了内容的逻辑结构。
【用户特别指令】：{requirement}
【导图素材结构内容】：
{rawContextData}";

        btnGlobalDraft.GetComponent<Image>().color = _colorGreen;
        btnGlobalDraft.GetComponentInChildren<TMP_Text>().text = "正在起草...";

        // 注意：全文起草是重量级操作，remember 传 false，不占用聊天记忆上下文！
        ExecuteToolPrompt(prompt, "GlobalDraft", false, userPrompt, (success, response) =>
        {
            ResetToolMode();
            if (AIChatManager.Instance != null)
                AIChatManager.Instance.AddSystemAIBubble(success ? FormatChineseArticle(response) : $"[起草失败: {response}]", false);
        });
    }

    // ==========================================
    // 工具执行引擎：支持回调自定义处理结果
    // ==========================================
    private string GetUserInput()
    {
        if (AIChatManager.Instance == null || AIChatManager.Instance.chatInput == null) return "";
        string txt = AIChatManager.Instance.chatInput.text;
        AIChatManager.Instance.chatInput.text = "";
        return txt;
    }

    // 【新增 userOriginalRequest 参数和 remember 参数】
    private void ExecuteToolPrompt(string finalPrompt, string eventName, bool remember, string userOriginalRequest = "", System.Action<bool, string> customCallback = null)
    {
        // 【ML 闭环极简判断】：是否有用户主动输入的自定义 Prompt
        if (InterventionTracker.Instance != null)
        {
            InterventionTracker.Instance.SetAIProcessing(true);

            if (string.IsNullOrWhiteSpace(userOriginalRequest))
                InterventionTracker.Instance.OnImplicitScaffoldAccepted(eventName); // 没打字，+0.5
            else
                InterventionTracker.Instance.OnCoCreationAccepted(eventName);      // 打字了，+1.5
        }

        if (UserBehaviorSystem.Instance != null) UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Local, "Copilot", eventName, 1);

        if (remember && !string.IsNullOrWhiteSpace(userOriginalRequest) && LLMManager.Instance != null)
        {
            LLMManager.Instance.AddToHistory("user", userOriginalRequest);
        }

        LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
        {
            if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(false);

            if (customCallback != null)
            {
                customCallback.Invoke(success, response);
            }
            else
            {
                AIChatManager.Instance.AddSystemAIBubble(success ? response : $"[执行失败: {response}]", remember);
            }
        });
    }

    // ==========================================
    // 5 大按钮功能实现
    // ==========================================
    private void OnLocalRefineClicked()
    {
        if (_currentlyGlowingBtn == btnLocalRefine)
        {
            var glow = _currentlyGlowingBtn.GetComponent<AINodeGlowEffect>();
            if (glow != null) glow.StopGlow();
            _currentlyGlowingBtn = null;
        }

        // 【ML 闭环】：二次点击取消黄色状态，意味着 "-1.0 强拒绝"
        if (_currentMode == ToolMode.ExpandWaiting)
        {
            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnInterventionRejected("article_refine");

            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.ExpandWaiting;

        btnContextExpand.GetComponent<Image>().color = _colorYellow;
        btnContextExpand.GetComponentInChildren<TMP_Text>().text = "请选取并输入...";

        TryGenerateQuoteBubble();
    }

    private void OnContextExpandClicked()
    {
        // 消除发光状态
        if (_currentlyGlowingBtn == btnContextExpand) {
            var glow = _currentlyGlowingBtn.GetComponent<AINodeGlowEffect>();
            if (glow != null) glow.StopGlow();
            _currentlyGlowingBtn = null;
        }

        // 【ML 闭环】：二次点击取消黄色状态，意味着 "-1.0 强拒绝"
        if (_currentMode == ToolMode.ExpandWaiting) 
        {
            if (InterventionTracker.Instance != null) 
                InterventionTracker.Instance.OnInterventionRejected("article_expand");
                
            ResetToolMode();
            return;
        }

        ResetToolMode(); 
        _currentMode = ToolMode.ExpandWaiting;
        
        btnContextExpand.GetComponent<Image>().color = _colorYellow;
        btnContextExpand.GetComponentInChildren<TMP_Text>().text = "请定位并输入...";

        TryGenerateQuoteBubble();
    }

    private void OnContextTransitionClicked()
    {
        if (_currentlyGlowingBtn == btnContextTransition)
        {
            var glow = _currentlyGlowingBtn.GetComponent<AINodeGlowEffect>();
            if (glow != null) glow.StopGlow();
            _currentlyGlowingBtn = null;
        }

        // 【ML 闭环】：二次点击取消黄色状态，意味着 "-1.0 强拒绝"
        if (_currentMode == ToolMode.ExpandWaiting)
        {
            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnInterventionRejected("article_stitch");

            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.ExpandWaiting;

        btnContextExpand.GetComponent<Image>().color = _colorYellow;
        btnContextExpand.GetComponentInChildren<TMP_Text>().text = "请定位并输入...";

        TryGenerateQuoteBubble();
    }

    private void OnGlobalReviewClicked()
    {
        if (_currentMode == ToolMode.ReviewWaiting)
        {
            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.ReviewWaiting;

        btnGlobalReview.GetComponent<Image>().color = _colorYellow;
        btnGlobalReview.GetComponentInChildren<TMP_Text>().text = "请输入侧重点...";

        // 审稿不需要选中文本，所以直接触发呼吸灯引导用户去打字
        if (AIChatManager.Instance != null) AIChatManager.Instance.StartChatInputGlow();
    }

    private void OnGlobalDraftClicked()
    {
        // 消除发光状态 (如果正在发光)
        if (_currentlyGlowingBtn == btnGlobalDraft)
        {
            var glow = _currentlyGlowingBtn.GetComponent<AINodeGlowEffect>();
            if (glow != null) glow.StopGlow();
            _currentlyGlowingBtn = null;
        }

        // 【ML 闭环】：二次点击取消黄色状态，意味着 "-1.0 强拒绝"
        if (_currentMode == ToolMode.DraftWaiting)
        {
            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnInterventionRejected("article_coldstart");

            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.DraftWaiting;

        btnGlobalDraft.GetComponent<Image>().color = _colorYellow;
        btnGlobalDraft.GetComponentInChildren<TMP_Text>().text = "请补充起草要求...";

        // 闪烁金光，引导用户输入
        if (AIChatManager.Instance != null) AIChatManager.Instance.StartChatInputGlow();
    }

    // ==========================================
    // 树结构读取与排版算法 (原封不动平移自 3.0)
    // ==========================================
    private string ConcatTreeData()
    {
        if (NodeCardManager.Instance == null) return "";
        var rootNodes = NodeCardManager.Instance.GetAllNodes().Where(n => n.parentNode == null).ToList();
        rootNodes.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        bool filterBySelection = NodeCardManager.Instance.HasSelection();
        var selectedNodes = NodeCardManager.Instance.GetSelectedNodes();

        foreach (var root in rootNodes)
            AppendNodeDFS(root, sb, 0, selectedNodes, filterBySelection);

        return sb.ToString().Trim();
    }

    private void AppendNodeDFS(BaseNodeController node, System.Text.StringBuilder sb, int depth, List<BaseNodeController> selectedNodes, bool filterBySelection)
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
            foreach (var child in node.childNodes.OrderByDescending(c => c.transform.position.y))
                AppendNodeDFS(child, sb, depth + 1, selectedNodes, filterBySelection);
        }
    }

    private string FormatChineseArticle(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return "";
        string[] lines = rawText.Replace("\r", "").Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
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
    // 【拼图三】：接管拖拽生成事件 (Drag & Drop)
    // ==========================================
    private void HandleNodeDroppedEvent(int insertIndex, string title, string content, string nodeID)
    {
        if (ArticleGenerator.Instance == null || ArticleGenerator.Instance.mainBodyInput == null) return;

        // 1. 在正文区插入加载占位符
        string placeholderText = $"\n[ AI 正在将节点【{title}】展开为正文... ]\n";
        string originalText = ArticleGenerator.Instance.mainBodyInput.text;
        ArticleGenerator.Instance.mainBodyInput.text = originalText.Insert(insertIndex, placeholderText);

        string prompt = $@"你是一个学术写作助手。用户将思维导图的一个节点拖入了文章中。
请根据以下节点的标题和内容，扩写成一段自然流畅的正文段落，用于无缝插入到文章中。
【节点标题】：{title}
【节点内容】：{content}
【规则】：直接输出扩写后的段落，不要废话，不要输出标签。";

        if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(true);

        // 2. 发起请求并替换占位符
        LLMManager.Instance.TaskChat(prompt, (response, success) =>
        {
            if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(false);

            string finalResult = success ? FormatChineseArticle(response) : $"\n[ 节点展开失败: {response} ]\n";

            if (ArticleGenerator.Instance != null && ArticleGenerator.Instance.mainBodyInput != null)
            {
                ArticleGenerator.Instance.mainBodyInput.text = ArticleGenerator.Instance.mainBodyInput.text.Replace(placeholderText, "\n" + finalResult + "\n");
            }

            if (UserBehaviorSystem.Instance != null && success)
            {
                UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Node, "Article", $"DragDropNode_{nodeID}", 1);
            }
        });
    }

    // ==========================================
    // 供主动介入系统 (AUBIM) 调用的智能引导接口
    // ==========================================
    public void TriggerProactiveGlow()
    {
        if (!_isModalOpen || ArticleGenerator.Instance == null) return;

        GameObject targetBtn = null;
        var input = ArticleGenerator.Instance.mainBodyInput;
        bool isFocused = input != null && input.isFocused;

        if (_hasText && _wordCount >= 500 && !isFocused) targetBtn = btnGlobalReview;
        else if (_hasSelection) targetBtn = btnLocalRefine;
        else if (!_hasText) targetBtn = btnGlobalDraft;
        else if (_isCaretInMiddle) targetBtn = btnContextTransition;
        else targetBtn = btnContextExpand;

        if (targetBtn != null && targetBtn.activeSelf)
        {
            // 假设您的发光脚本有 StartGlow 方法
            var glow = targetBtn.GetComponent<AINodeGlowEffect>();
            if (glow != null) glow.StartGlow(); // 如果您旧脚本叫 StartBreathing，请自行修改

            _currentlyGlowingBtn = targetBtn;

            // 【ML 闭环】：提前向 Tracker 注册这是一个主动推荐行为
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.SetLastPredictedRecordKey(GetMLKeyForBtn(targetBtn));
            }

            Debug.Log($"<color=cyan>[Copilot 智能引导]</color> 侦测到停滞，无感推荐：{targetBtn.name}");
        }
    }

    // 【ML 闭环】：处理 "-0.2 忽略"
    public void HandleUserTyping()
    {
        if (_currentlyGlowingBtn != null)
        {
            var glow = _currentlyGlowingBtn.GetComponent<AINodeGlowEffect>();
            if (glow != null) glow.StopGlow(); // 关掉发光（或您对应的停止方法）

            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionIgnored(GetMLKeyForBtn(_currentlyGlowingBtn));
            }
            _currentlyGlowingBtn = null;
        }
    }

    private string GetMLKeyForBtn(GameObject btn)
    {
        if (btn == btnGlobalDraft) return "article_coldstart";
        if (btn == btnLocalRefine) return "article_refine";
        if (btn == btnContextExpand) return "article_expand";
        if (btn == btnContextTransition) return "article_stitch";
        if (btn == btnGlobalReview) return "article_reflect";
        return "unknown";
    }
}