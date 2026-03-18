using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class ChatSuggestionManager : MonoBehaviour
{
    public static ChatSuggestionManager Instance;

    [Header("UI 绑定")]
    public GameObject chipPrefab;           // 刚才写好的气泡 Prefab
    public Transform chipContainer;         // 生成气泡的父节点 (建议挂载 VerticalLayoutGroup)
    public TMP_InputField chatPromptInput;  // 聊天区的输入框

    private Coroutine _currentLifecycleCoroutine;
    private List<GameObject> _activeChips = new List<GameObject>();

    // 记录是否有任何一个气泡正在被注视
    private bool _isAnyChipHovered = false;
    private float _aliveTimer = 0f;

    // 用于接收大模型解析出来的 3 个逼问数据
    public struct SuggestionData
    {
        public string ShortTitle;
        public string FullContent;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        // 订阅全局遥测事件
        if (UserBehaviorSystem.Instance != null) // 如果你的系统里是静态事件，可以直接写 UserBehaviorSystem.OnEventLogged += ...
        {
            UserBehaviorSystem.OnEventLogged += HandleGlobalUserEvent;
        }
    }

    void OnDisable()
    {
        // 注销订阅防内存泄漏
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.OnEventLogged -= HandleGlobalUserEvent;
        }
    }

    private void HandleGlobalUserEvent(TelemetryLog log)
    {
        // 如果当前没有任何气泡在等待或存活，直接无视
        if (_currentLifecycleCoroutine == null) return;

        string eType = log.EventType;

        // 如果侦测到任何在画布区 (Canvas) 或 成文区 (Article) 的实质性操作
        if (eType.StartsWith("Canvas_") || eType.StartsWith("Article_") || eType.StartsWith("Edit_") || eType.StartsWith("Node_") || eType.StartsWith("Object_"))
        {
            if(_activeChips.Count == 0)
            {
                // 情景 A：气泡还没露面，用户就进入心流去干活了
                Debug.Log($"<color=white>[Chat Suggestion]</color> 用户在气泡弹出前就切换了工作区 ({eType})。静默取消气泡生成，不产生 ML 惩罚！");
                ClearAllChips(); // 直接掐断协程，不扣分！
            }
            else
            {
                // 情景 B：气泡已经弹出来了，用户看了一眼（或没看），转头去旁边干活了
                Debug.Log($"<color=yellow>[Chat Suggestion]</color> 用户在气泡存活期切换了工作区 ({eType})，视为无视/搁置，记录 Ignored (-0.2分)！");

                if (InterventionTracker.Instance != null)
                {
                    InterventionTracker.Instance.OnInterventionIgnored("chat_socratic_chip");
                }
                ClearAllChips(); // 扣分并销毁
            }
        }
    }

    // ==========================================
    // 外部调用入口：当大模型回答完毕后，传入字数和解析好的逼问列表
    // ==========================================
    public void StartSuggestionLifecycle(int aiResponseWordCount, List<SuggestionData> suggestions)
    {
        // 清理上一波残留
        ClearAllChips();
        if (suggestions == null || suggestions.Count == 0) return;

        if (InterventionClassifier.Instance != null && NodeCardManager.Instance != null)
        {
            int totalNodes = NodeCardManager.Instance.GetAllNodes().Count;
            int selectedNodes = NodeCardManager.Instance.GetSelectedNodes().Count;

            // 调用大脑的裁决方法 (附带叛逆试探机制)
            bool shouldTrigger = InterventionClassifier.Instance.ShouldTriggerIntervention(
                "chat_socratic_chip", "Chat", totalNodes, selectedNodes, "None");

            if (!shouldTrigger)
            {
                Debug.Log("<color=orange>[AI Brain]</color> 大脑预测被试者目前极其反感聊天区逼问，取消本次气泡生成，避免激怒用户！");
                return; // 直接熔断，绝不打扰！
            }
        }

        if (_currentLifecycleCoroutine != null) StopCoroutine(_currentLifecycleCoroutine);
        _currentLifecycleCoroutine = StartCoroutine(LifecycleRoutine(aiResponseWordCount, suggestions));
    }

    private IEnumerator LifecycleRoutine(int wordCount, List<SuggestionData> suggestions)
    {
        // 1. 根据字数动态计算等待时间 (5 ~ 15秒)
        float baseWaitTime = Mathf.Clamp(wordCount / 12f, 5f, 80f);
        float tolerance = 0f;
        if (InterventionTracker.Instance != null)
        {
            tolerance = InterventionTracker.Instance.GetToleranceOffset("chat_socratic_chip");
        }

        float finalWaitTime = baseWaitTime + tolerance;
        float timer = 0f;

        Debug.Log($"<color=cyan>[Chat Suggestion]</color> 基础阅读期 {baseWaitTime:F1}s + 专属容忍度 {tolerance:F1}s = 最终等待 {finalWaitTime:F1}s 后弹出气泡...");

        // =========================================================
        // 【防 TMP 幽灵字符滤镜】
        // =========================================================
        string GetCleanText()
        {
            if (chatPromptInput == null) return "";
            return chatPromptInput.text.Replace("\u200B", "").Trim();
        }

        string lastKnownText = GetCleanText();

        // 【新增：单次防抖锁】保证每次 AI 回复，只记录一次抢答，绝不随打字数量无限叠加！
        bool hasTriggeredPreemptive = false;

        // 2. 第一阶段：隐忍等待期 (支持编辑时重置计时！)
        while (timer < finalWaitTime)
        {
            timer += Time.deltaTime;

            if (chatPromptInput != null && chatPromptInput.isFocused)
            {
                string currentCleanText = GetCleanText();

                // 只有纯净文本发生真实变化时，才算作人类敲击了键盘
                if (currentCleanText != lastKnownText)
                {
                    // =========================================================
                    // 【新增：认知抢答判定】
                    // 如果这是本轮等待期内，用户【第一次】输入真实字符，判定为抢答！
                    // =========================================================
                    if (!hasTriggeredPreemptive && currentCleanText.Length > 0)
                    {
                        hasTriggeredPreemptive = true; // 上锁，后续打字不再触发缩短时间

                        Debug.Log("<color=magenta>[Chat Suggestion]</color> 用户在气泡出现前就开始了输入！触发【认知抢答】，加快下次气泡的生成速度。");

                        if (InterventionTracker.Instance != null)
                        {
                            InterventionTracker.Instance.OnPreemptiveTyping("chat_socratic_chip");
                        }
                    }

                    Debug.Log("<color=yellow>[Chat Suggestion]</color> 用户正在输入纯净文本，重置逼问生成倒计时...");
                    timer = 0f; // 依然保留重置逻辑，只要手不停，气泡就不出
                    lastKnownText = currentCleanText;
                }
            }
            yield return null;
        }

        // 3. 第二阶段：生成气泡并淡入
        foreach (var data in suggestions)
        {
            GameObject chipObj = Instantiate(chipPrefab, chipContainer);
            ChatSuggestionChip chip = chipObj.GetComponent<ChatSuggestionChip>();
            if (chip != null) chip.Initialize(data.ShortTitle, data.FullContent, chatPromptInput);
            _activeChips.Add(chipObj);
        }

        Canvas.ForceUpdateCanvases(); // 强行撑开布局防重叠

        string textBeforeFade = chatPromptInput != null ? chatPromptInput.text.Trim() : "";
        yield return StartCoroutine(FadeChips(0f, 0.8f, 1f));

        // 检查这 1 秒内文本是否发生了变化
        if (chatPromptInput != null && chatPromptInput.isFocused)
        {
            if (chatPromptInput.text.Trim() != textBeforeFade)
            {
                Debug.Log("<color=white>[Chat Suggestion]</color> 用户在气泡淡入动画期间开始打字，属于【时机撞车】。静默销毁气泡，防止数据污染！");
                ClearAllChips();
                yield break; // 彻底了结协程，不扣分也不加分！
            }
        }

        // 4. 第三阶段：15秒存活倒计时
        _aliveTimer = 0f;
        _isAnyChipHovered = false;

        // 记录气泡生成这一瞬间，Prompt 框里的文字
        string textWhenBubblesSpawned = chatPromptInput != null ? chatPromptInput.text : "";

        float cognitiveReactionBuffer = 3f;

        while (_aliveTimer < 15f)
        {
            if (!_isAnyChipHovered) _aliveTimer += Time.deltaTime;

            // =========================================================
            // 【ML 逻辑重构：灵感激发判定】
            // 监控文本变化：只要文本变了，说明被试者被气泡激活，开始自己打字了！
            // =========================================================
            if (chatPromptInput != null && chatPromptInput.isFocused)
            {
                if (chatPromptInput.text != textWhenBubblesSpawned)
                {
                    if (_aliveTimer < cognitiveReactionBuffer)
                    {
                        Debug.Log($"<color=white>[Chat Suggestion]</color> 气泡存活仅 {_aliveTimer:F2}s 用户便开始输入，低于人类认知反应阈值。判定为【动作撞车】，静默销毁！");
                        ClearAllChips();
                        yield break; // 彻底了结协程，不产生任何 ML 记录！
                    }

                    bool isTypingSuggestion = false;
                    string currentInput = chatPromptInput.text.Trim();

                    // 防抖：输入至少 2 个字符才开始判定
                    if (currentInput.Length >= 2)
                    {
                        foreach (var suggestion in suggestions)
                        {
                            if (currentInput.Contains(suggestion.ShortTitle) ||
                                suggestion.FullContent.Contains(currentInput))
                            {
                                isTypingSuggestion = true;
                                break;
                            }
                        }
                    }

                    if (isTypingSuggestion)
                    {
                        // 情景 A：用户在手动抄写建议（虽然这很罕见，但也算采纳）
                        Debug.Log("<color=green>[ML Tracker]</color> 用户正在手动抄写气泡内容，视为【显性采纳】！");
                        if (InterventionTracker.Instance != null) InterventionTracker.Instance.OnButtonClicked("chat_socratic_chip");
                    }
                    else
                    {
                        // =========================================================
                        // 【核心观念扭转】：这不再是拒绝！这是灵感被激发！
                        // 情景 B：用户发呆了很久，看到气泡后，突然开始输入自己的全新内容。
                        // 我们有理由相信是气泡的苏格拉底式提问促使了他去回答！
                        // =========================================================
                        Debug.Log("<color=magenta>[ML Tracker]</color> 气泡打破了发呆，用户开始自主输入新内容！视为【隐性采纳/灵感激发】。记录 Implicit_Scaffold_Accepted (+0.5分)");
                        if (InterventionTracker.Instance != null) InterventionTracker.Instance.OnImplicitScaffoldAccepted("chat_socratic_chip");
                    }

                    // 判定完毕后，气泡功成身退，立刻销毁，绝不挡着用户打字！
                    ClearAllChips();
                    yield break;
                }
            }
            yield return null;
        }

        // 5. 第四阶段：15秒超时，自然死亡
        // =========================================================
        // 【ML 细分 2：被动无视 (-0.2分)】
        // 15 秒结束，用户既没有点气泡，也没有自己打字，仅仅是没理它。
        // =========================================================
        Debug.Log("<color=yellow>[ML Tracker]</color> 逼问气泡 15 秒超时自然消散，视为【无视/搁置】。记录 Ignored (-0.2分)");

        if (InterventionTracker.Instance != null)
        {
            InterventionTracker.Instance.OnInterventionIgnored("chat_socratic_chip");
        }

        yield return StartCoroutine(FadeChips(0.8f, 0f, 0.5f));
        ClearAllChips();
    }

    private IEnumerator FadeChips(float startAlpha, float endAlpha, float duration)
    {
        float t = 0;
        List<CanvasGroup> cgs = new List<CanvasGroup>();
        foreach (var chip in _activeChips)
        {
            if (chip != null) cgs.Add(chip.GetComponent<CanvasGroup>());
        }

        while (t < duration)
        {
            t += Time.deltaTime;
            float normalizedTime = t / duration;
            foreach (var cg in cgs)
            {
                if (cg != null) cg.alpha = Mathf.Lerp(startAlpha, endAlpha, normalizedTime);
            }
            yield return null;
        }
    }

    public void ClearAllChips()
    {
        if (_currentLifecycleCoroutine != null)
        {
            StopCoroutine(_currentLifecycleCoroutine);
            _currentLifecycleCoroutine = null; // 核心加固：置空防多次触发
        }
        foreach (var chip in _activeChips)
        {
            if (chip != null) Destroy(chip);
        }
        _activeChips.Clear();
    }

    // 接收来自 Chip 的悬停状态汇报
    public void SetHoverState(bool isHovering)
    {
        _isAnyChipHovered = isHovering;
        if (isHovering)
        {
            // 只要一摸到气泡，立刻“续命”，倒计时清零重置为完整的 15 秒！
            _aliveTimer = 0f;
        }
    }

    // ==========================================
    // 幽灵监控：判断是“直接采纳(+1.0)”还是“共创修改(+1.5)”
    // ==========================================
    public void TrackCoCreationSubmission(string originalContent)
    {
        StartCoroutine(CoCreationRoutine(originalContent));
    }

    private IEnumerator CoCreationRoutine(string originalContent)
    {
        // 核心修复：提前把原句的隐形空格和换行剃干净
        string cleanOriginal = originalContent.Trim();
        bool isEdited = false;

        while (chatPromptInput != null && !string.IsNullOrEmpty(chatPromptInput.text))
        {
            // 核心修复：实时获取当前输入框的文字，并剃除因为按回车瞬间产生的 \n
            string currentCleanText = chatPromptInput.text.Trim();

            // 1. 严格侦测：只有当纯净文本的长度或内容发生实质性变化时，才算共创！
            if (!isEdited && currentCleanText != cleanOriginal)
            {
                isEdited = true;
                Debug.Log("<color=magenta>[Tracker]</color> 侦测到实质性的字数/内容修改，锁定为【深度共创】！");
            }

            // 2. 侦测发送行为 (回车发送)
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                // 延迟一帧，等待原本的 Chat 聊天发送逻辑把输入框彻底清空
                yield return null;

                if (string.IsNullOrEmpty(chatPromptInput.text))
                {
                    // 确认发送成功！进行终极结算！
                    if (isEdited)
                    {
                        Debug.Log("<color=cyan>[ML]</color> 用户修改了逼问气泡，记录 Co_Creation (+1.5分)！");
                        InterventionTracker.Instance.OnCoCreationAccepted("chat_socratic_chip");
                    }
                    else
                    {
                        Debug.Log("<color=green>[ML]</color> 用户一字未改直接回车，记录 Explicit_Adopt (+1.0分)！");
                        InterventionTracker.Instance.OnButtonClicked("chat_socratic_chip");
                    }

                    yield break; // 功成身退
                }
            }
            yield return null;
        }
    }
}