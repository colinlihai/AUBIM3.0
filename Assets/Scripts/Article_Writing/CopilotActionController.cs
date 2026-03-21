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

    private string _proactiveSourceEvent = "";

    private AIChatManager.ChatBubbleTracker _pendingQuoteBubble;
    private string _lastQuotedText = "";
    private Coroutine _quoteCoroutine;

    private bool _isModalOpen = false;

    public enum ToolMode { None, ExpandWaiting, TransitionWaiting, RefineWaiting, ReviewWaiting, DraftWaiting }
    private ToolMode _currentMode = ToolMode.None;

    // 缓存按钮的原始颜色，用于复原
    private Color _btnOriginalColor = new Color(0.4f, 0.4f, 0.4f, 1f); // 浅灰
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

    public bool IsButtonCurrentlyGlowing(GameObject btnObj)
    {
        return _currentlyGlowingBtn != null && _currentlyGlowingBtn == btnObj;
    }

    public bool IsButtonActiveTool(GameObject btnObj)
    {
        if (_currentMode == ToolMode.RefineWaiting && btnObj == btnLocalRefine) return true;
        if (_currentMode == ToolMode.ExpandWaiting && btnObj == btnContextExpand) return true;
        if (_currentMode == ToolMode.TransitionWaiting && btnObj == btnContextTransition) return true;
        if (_currentMode == ToolMode.ReviewWaiting && btnObj == btnGlobalReview) return true;
        if (_currentMode == ToolMode.DraftWaiting && btnObj == btnGlobalDraft) return true;

        return false; // 对于“打开成文区”这类非工具按钮，永远返回 false 即可正常变色
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

    private void ClearPendingQuoteBubble()
    {
        if (_pendingQuoteBubble != null && AIChatManager.Instance != null)
        {
            AIChatManager.Instance.RemoveSpecificBubble(_pendingQuoteBubble);
        }
        _pendingQuoteBubble = null;
        _lastQuotedText = "";
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
            quoteToGenerate = $"你要续写的前文为：\n...{_contextBefore}";
        }
        else if (_hasText && _currentMode == ToolMode.TransitionWaiting)
        {
            // 内容衔接最好在中间。如果用户非要在末尾点内容衔接，依然给出兼容提示
            if (_isCaretInMiddle)
                quoteToGenerate = $"你要关联的上下文为：\n...{_contextBefore} | {_contextAfter}...";
            else
                quoteToGenerate = $"你要关联的上下文为：\n...{_contextBefore} | [文章末尾无下文]";
        }

        if (string.IsNullOrWhiteSpace(quoteToGenerate)) return;

        // 掐断之前正在等待的协程，开启新的防抖等待
        if (_quoteCoroutine != null) StopCoroutine(_quoteCoroutine);
        _quoteCoroutine = StartCoroutine(WaitMouseUpAndGenerate(quoteToGenerate));
    }

    private IEnumerator WaitMouseUpAndGenerate(string textToQuote)
    {
        // 只要鼠标左键还在按着（用户还在拖拽调整选区），就一直挂起不执行！
        while (Input.GetMouseButton(0))
        {
            yield return null;
        }

        // 鼠标松开后，检查内容是否和上次一模一样，如果一样就不重复生成
        if (textToQuote == _lastQuotedText) yield break;

        // 【气泡替换核心】：在生成新气泡前，先销毁上一次未确认的旧气泡，保证聊天区干净
        ClearPendingQuoteBubble();

        _lastQuotedText = textToQuote;

        if (AIChatManager.Instance != null)
        {
            // 生成气泡并记录追踪凭证 (建议此处传 false，因为还没确认提交，先不污染大模型记忆)
            _pendingQuoteBubble = AIChatManager.Instance.AddContextQuoteBubble(textToQuote, false);

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
        ClearPendingQuoteBubble();

        _currentMode = ToolMode.None;
        _proactiveSourceEvent = "";
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
        if (_currentMode == ToolMode.None) return false;
        string cleanPrompt = userPrompt != null ? userPrompt.Trim() : "";
        // ========================================================
        // 【ML 漏斗阶段 3 & 4：终极转化结算！】
        // ========================================================
        if (!string.IsNullOrEmpty(_proactiveSourceEvent) && InterventionTracker.Instance != null)
        {
            if (string.IsNullOrWhiteSpace(cleanPrompt))
            {
                // 没打字，直接回车
                InterventionTracker.Instance.OnImplicitScaffoldAccepted(_proactiveSourceEvent);
                Debug.Log($"<color=green>[ML]</color> 用户留空回车，采纳了 {_proactiveSourceEvent} 的默认提示词。记录隐性脚手架采纳 (+0.5)");
            }
            else
            {
                // 打了字，加了额外要求
                InterventionTracker.Instance.OnCoCreationAccepted(_proactiveSourceEvent);
                Debug.Log($"<color=cyan>[ML]</color> 用户补充了指令 '{cleanPrompt}'，发生了深度的 {_proactiveSourceEvent}！记录深度共创 (+1.5)");
            }
        }

        if (!string.IsNullOrWhiteSpace(cleanPrompt))
        {
            if (_currentMode == ToolMode.RefineWaiting || _currentMode == ToolMode.ExpandWaiting || _currentMode == ToolMode.TransitionWaiting)
            {
                // 有引用气泡的三剑客：直接将文字追加入气泡尾部
                if (_pendingQuoteBubble != null && AIChatManager.Instance != null)
                {
                    AIChatManager.Instance.AppendTextToBubble(_pendingQuoteBubble, cleanPrompt);
                }
            }
            else if (_currentMode == ToolMode.DraftWaiting || _currentMode == ToolMode.ReviewWaiting)
            {
                // 全文起草 / 全局审阅：生成带功能前缀的独立气泡
                if (AIChatManager.Instance != null)
                {
                    string actionName = _currentMode == ToolMode.ReviewWaiting ? "审稿意见" : "全文起草";

                    // UI 上显示的带颜色富文本，例如：[全文起草]：用诙谐的语气
                    string displayPrompt = $"<color=#00e5ff><b>[{actionName}]：</b></color> {cleanPrompt}";

                    // 存入大模型上下文的纯净文本（带上前缀也有助于大模型理解上下文）
                    string historyPrompt = $"[{actionName}] {cleanPrompt}";

                    AIChatManager.Instance.AddStandardUserBubble(displayPrompt, historyPrompt, true);
                }
            }
        }

        _pendingQuoteBubble = null;
        _lastQuotedText = "";

        // 执行具体的功能，Execute 方法执行完后，它们内部会自动调用 ResetToolMode()，从而清空追踪器。
        if (_currentMode == ToolMode.RefineWaiting) ExecuteRefine(userPrompt);
        else if (_currentMode == ToolMode.ExpandWaiting) ExecuteExpand(userPrompt);
        else if (_currentMode == ToolMode.TransitionWaiting) ExecuteTransition(userPrompt);
        else if (_currentMode == ToolMode.ReviewWaiting) ExecuteReview(userPrompt);
        else if (_currentMode == ToolMode.DraftWaiting) ExecuteDraft(userPrompt); // 或者是 GlobalDraftWaiting，以你实际的枚举名称为准

        return true;
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

    private void ExecuteToolPrompt(string finalPrompt, string eventName, bool remember, string userOriginalRequest = "", System.Action<bool, string> customCallback = null)
    {
        // 1. 设置 AI 工作状态锁定 (保留：因为无论是手动还是被动，只要 AI 在跑，就得锁定界面防抖)
        if (InterventionTracker.Instance != null)
        {
            InterventionTracker.Instance.SetAIProcessing(true);
        }

        // 2. 基础遥测埋点 (保留：这只是用来记录用户用了什么功能，不影响 AI 的 ML 评分)
        if (UserBehaviorSystem.Instance != null)
            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.Article_Generate_Local, "Copilot", eventName, 1);

        // 3. 存入聊天历史记忆
        if (remember && !string.IsNullOrWhiteSpace(userOriginalRequest) && LLMManager.Instance != null)
        {
            LLMManager.Instance.AddToHistory("user", userOriginalRequest);
        }

        // 4. 调用大模型发起请求
        LLMManager.Instance.TaskChat(finalPrompt, (response, success) =>
        {
            // 释放 AI 工作状态
            if (InterventionTracker.Instance != null) InterventionTracker.Instance.SetAIProcessing(false);

            if (customCallback != null)
            {
                customCallback.Invoke(success, response);
            }
            else
            {
                if (AIChatManager.Instance != null)
                    AIChatManager.Instance.AddSystemAIBubble(success ? response : $"[执行失败: {response}]", remember);
            }
        });
    }

    // ==========================================
    // 5 大按钮功能实现 (完美对接 ML 漏斗与 UI 呼吸灯)
    // ==========================================
    private void OnLocalRefineClicked()
    {
        bool wasGlowing = false;

        if (_currentlyGlowingBtn == btnLocalRefine)
        {
            // 【核心修复 1】：使用内部强力熄灭方法，彻底废弃 AINodeGlowEffect
            StopGlowEffect();
            wasGlowing = true;

            // 【核心修复 2】：记录 ML 漏斗第一步 —— 成功吸引点击 (+1.0分)
            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnButtonClicked("article_refine");
        }

        if (_currentMode == ToolMode.RefineWaiting)
        {
            if (!string.IsNullOrEmpty(_proactiveSourceEvent) && InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionRejected(_proactiveSourceEvent);
                Debug.Log($"<color=orange>[ML]</color> 用户在考虑后放弃了 {_proactiveSourceEvent}，记录显性拒绝 (-1.0)");
            }

            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.RefineWaiting;
        _proactiveSourceEvent = wasGlowing ? "article_refine" : "";
        btnLocalRefine.GetComponent<Image>().color = _colorYellow;
        btnLocalRefine.GetComponentInChildren<TMP_Text>().text = "请选取并输入...";

        TryGenerateQuoteBubble();
    }

    private void OnContextExpandClicked()
    {
        bool wasGlowing = false;

        if (_currentlyGlowingBtn == btnContextExpand)
        {
            StopGlowEffect();
            wasGlowing = true;

            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnButtonClicked("article_expand");
        }

        if (_currentMode == ToolMode.ExpandWaiting)
        {
            if (!string.IsNullOrEmpty(_proactiveSourceEvent) && InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionRejected(_proactiveSourceEvent);
                Debug.Log($"<color=orange>[ML]</color> 用户在考虑后放弃了 {_proactiveSourceEvent}，记录显性拒绝 (-1.0)");
            }
            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.ExpandWaiting;
        _proactiveSourceEvent = wasGlowing ? "article_expand" : "";
        btnContextExpand.GetComponent<Image>().color = _colorYellow;
        btnContextExpand.GetComponentInChildren<TMP_Text>().text = "请定位并输入...";

        TryGenerateQuoteBubble();
    }

    private void OnContextTransitionClicked()
    {
        bool wasGlowing = false;

        if (_currentlyGlowingBtn == btnContextTransition)
        {
            StopGlowEffect();
            wasGlowing = true;

            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnButtonClicked("article_stitch");
        }

        if (_currentMode == ToolMode.TransitionWaiting)
        {
            if (!string.IsNullOrEmpty(_proactiveSourceEvent) && InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionRejected(_proactiveSourceEvent);
                Debug.Log($"<color=orange>[ML]</color> 用户在考虑后放弃了 {_proactiveSourceEvent}，记录显性拒绝 (-1.0)");
            }
            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.TransitionWaiting;
        _proactiveSourceEvent = wasGlowing ? "article_stitch" : "";
        btnContextTransition.GetComponent<Image>().color = _colorYellow;
        btnContextTransition.GetComponentInChildren<TMP_Text>().text = "请定位并输入...";

        TryGenerateQuoteBubble();
    }

    private void OnGlobalReviewClicked()
    {
        bool wasGlowing = false;

        if (_currentlyGlowingBtn == btnGlobalReview)
        {
            StopGlowEffect();
            wasGlowing = true;

            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnButtonClicked("article_reflect");
        }

        if (_currentMode == ToolMode.ReviewWaiting)
        {
            if (!string.IsNullOrEmpty(_proactiveSourceEvent) && InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionRejected(_proactiveSourceEvent);
                Debug.Log($"<color=orange>[ML]</color> 用户在考虑后放弃了 {_proactiveSourceEvent}，记录显性拒绝 (-1.0)");
            }
            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.ReviewWaiting;
        _proactiveSourceEvent = wasGlowing ? "article_reflect" : "";
        btnGlobalReview.GetComponent<Image>().color = _colorYellow;
        btnGlobalReview.GetComponentInChildren<TMP_Text>().text = "请输入侧重点...";

        // 审稿不需要选中文本，所以直接触发呼吸灯引导用户去打字
        if (AIChatManager.Instance != null) AIChatManager.Instance.StartChatInputGlow();
    }

    private void OnGlobalDraftClicked()
    {
        bool wasGlowing = false;

        if (_currentlyGlowingBtn == btnGlobalDraft)
        {
            StopGlowEffect();
            wasGlowing = true;

            if (InterventionTracker.Instance != null)
                InterventionTracker.Instance.OnButtonClicked("article_coldstart");
        }

        if (_currentMode == ToolMode.DraftWaiting) // 注意：如果你的枚举是 GlobalDraftWaiting，请自行替换
        {
            if (!string.IsNullOrEmpty(_proactiveSourceEvent) && InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionRejected(_proactiveSourceEvent);
                Debug.Log($"<color=orange>[ML]</color> 用户在考虑后放弃了 {_proactiveSourceEvent}，记录显性拒绝 (-1.0)");
            }
            ResetToolMode();
            return;
        }

        ResetToolMode();
        _currentMode = ToolMode.DraftWaiting; // 注意：同上
        _proactiveSourceEvent = wasGlowing ? "article_coldstart" : "";
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

        // 【核心修复】：彻底删除 filterBySelection 和 selectedNodes 的获取逻辑，强制获取全图
        foreach (var root in rootNodes)
            AppendNodeDFS(root, sb, 0);

        return sb.ToString().Trim();
    }

    // 【核心修复】：移除 selectedNodes 和 filterBySelection 参数，实现无差别遍历
    private void AppendNodeDFS(BaseNodeController node, System.Text.StringBuilder sb, int depth)
    {
        if (node == null || !node.gameObject.activeSelf) return;

        // 只要节点存在，就提取它的标题和内容，无视选中状态
        string indent = new string(' ', depth * 4);
        if (!string.IsNullOrWhiteSpace(node.Data.Title)) sb.AppendLine($"{indent}# {node.Data.Title}");
        if (!string.IsNullOrWhiteSpace(node.Data.Content)) sb.AppendLine($"{indent}{node.Data.Content.Replace("\n", "\n" + indent)}");
        sb.AppendLine();

        if (node.childNodes != null && node.childNodes.Count > 0)
        {
            foreach (var child in node.childNodes.OrderByDescending(c => c.transform.position.y))
                AppendNodeDFS(child, sb, depth + 1);
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
    // UI 按钮专属闪烁控制 (完全听从大脑指令，并突破渲染锁)
    // ==========================================
    private Coroutine _glowCoroutine;
    public bool IsAnyButtonGlowing => _currentlyGlowingBtn != null;

    // 【核心修复 1】：接收大脑传来的 mlKey (aiSuggestionType)
    public void TriggerProactiveGlow(string aiSuggestionType = "")
    {
        if (!_isModalOpen || ArticleGenerator.Instance == null) return;

        Debug.Log($"<color=cyan>[Copilot 智能引导]</color> 侦测到停滞，尝试点亮推荐：{aiSuggestionType}");

        GameObject targetBtn = btnGlobalDraft; // 默认保底给全文起草

        // 【核心修复 2】：绝对服从大脑的预测结果进行精准映射！
        if (!string.IsNullOrEmpty(aiSuggestionType))
        {
            string lower = aiSuggestionType.ToLower();
            if (lower.Contains("refine")) targetBtn = btnLocalRefine;
            else if (lower.Contains("expand")) targetBtn = btnContextExpand;
            else if (lower.Contains("stitch") || lower.Contains("transition")) targetBtn = btnContextTransition;
            else if (lower.Contains("reflect") || lower.Contains("review")) targetBtn = btnGlobalReview;
            else if (lower.Contains("coldstart") || lower.Contains("draft")) targetBtn = btnGlobalDraft;
        }

        // 如果被选中的按钮当前处于隐藏状态，则放弃闪烁
        if (targetBtn == null || !targetBtn.activeInHierarchy)
        {
            Debug.LogWarning($"<color=orange>[Copilot]</color> 目标按钮不可见，取消本次推荐闪烁。");
            if (InterventionTracker.Instance != null) InterventionTracker.Instance.AbortLocalBreathing();
            return;
        }

        // 停止可能存在的上一个闪烁
        StopGlowEffect();

        _currentlyGlowingBtn = targetBtn;

        // 【核心修复 3】：不再使用 AINodeGlowEffect，而是开启专属的强力 UI 呼吸协程
        _glowCoroutine = StartCoroutine(ButtonBreathRoutine(targetBtn));
    }

    public void StopGlowEffect()
    {
        if (_currentlyGlowingBtn != null)
        {
            Image img = _currentlyGlowingBtn.GetComponent<Image>();
            if (img != null)
            {
                img.color = _btnOriginalColor; // 恢复按钮本色
                // 【突破渲染锁】：强制清空 CanvasRenderer 的颜色覆写！
                img.CrossFadeColor(Color.white, 0f, true, true);
            }
            _currentlyGlowingBtn = null;
        }
        if (_glowCoroutine != null)
        {
            StopCoroutine(_glowCoroutine);
            _glowCoroutine = null;
        }
    }

    private IEnumerator ButtonBreathRoutine(GameObject btnObj)
    {
        Image img = btnObj.GetComponent<Image>();
        if (img == null) yield break;

        Color baseColor = _btnOriginalColor;
        Color glowColor = new Color(1f, 0.8f, 0.2f, 1f); // 耀眼金

        float timer = 0f;
        float elapsed = 0f;

        // 持续闪烁 15 秒存活期
        while (elapsed < 15f && _currentlyGlowingBtn == btnObj)
        {
            timer += Time.deltaTime * 3f;
            elapsed += Time.deltaTime;
            float lerp = (Mathf.Sin(timer) + 1f) / 2f;

            img.color = Color.Lerp(baseColor, glowColor, lerp);

            // 【突破渲染锁】：确保每一帧的颜色变化都能冲破 Unity Button 的死锁并被渲染出来
            img.CrossFadeColor(Color.white, 0f, true, true);

            yield return null;
        }

        // 如果 15 秒走完，且用户没有点击它
        if (_currentlyGlowingBtn == btnObj)
        {
            StopGlowEffect();
            Debug.Log("<color=yellow>[Copilot]</color> 按钮闪烁 15 秒超时，用户未理睬，自动熄灭。");

            // 通知 ML Tracker 被搁置无视 (-0.2分)
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.OnInterventionIgnored();
            }
        }
    }

    public void HandleUserTyping()
    {
        if (IsAnyButtonGlowing)
        {
            Debug.Log("<color=yellow>[Copilot]</color> 侦测到用户主动输入，打断 AI 闪烁推荐。");

            // 熄灭 UI 颜色
            StopGlowEffect();

            // 通知大脑
            if (InterventionTracker.Instance != null)
            {
                InterventionTracker.Instance.AbortLocalBreathing();
            }
        }
    }
}