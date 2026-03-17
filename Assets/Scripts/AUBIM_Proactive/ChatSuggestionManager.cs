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

    // ==========================================
    // 外部调用入口：当大模型回答完毕后，传入字数和解析好的逼问列表
    // ==========================================
    public void StartSuggestionLifecycle(int aiResponseWordCount, List<SuggestionData> suggestions)
    {
        // 清理上一波残留
        ClearAllChips();

        if (suggestions == null || suggestions.Count == 0) return;

        if (_currentLifecycleCoroutine != null) StopCoroutine(_currentLifecycleCoroutine);
        _currentLifecycleCoroutine = StartCoroutine(LifecycleRoutine(aiResponseWordCount, suggestions));
    }

    private IEnumerator LifecycleRoutine(int wordCount, List<SuggestionData> suggestions)
    {
        // 1. 根据字数动态计算等待时间 (5 ~ 15秒)
        float waitTime = Mathf.Clamp(wordCount / 20f, 5f, 15f);
        float timer = 0f;

        Debug.Log($"<color=cyan>[Chat Suggestion]</color> AI 回复 {wordCount} 字，等待 {waitTime:F1} 秒后弹出逼问气泡...");

        // 【核心修复 1】：记录刚开始倒计时时的文本内容
        string lastKnownText = chatPromptInput != null ? chatPromptInput.text : "";

        // 2. 第一阶段：隐忍等待期 (支持编辑时重置计时！)
        while (timer < waitTime)
        {
            timer += Time.deltaTime;

            if (chatPromptInput != null && chatPromptInput.isFocused)
            {
                // =========================================================
                // 【核心修复 2】：抛弃 Input.anyKeyDown，直接比对底层字符串！
                // 只要文字发生了变化，说明被试者正在疯狂打字，立刻把计时器砸回 0！
                // =========================================================
                if (chatPromptInput.text != lastKnownText)
                {
                    Debug.Log("<color=yellow>[Chat Suggestion]</color> 用户正在输入，重置逼问生成倒计时...");
                    timer = 0f; // 彻底清零重置！
                    lastKnownText = chatPromptInput.text; // 更新比对基准，为下一次敲击做准备
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
        yield return StartCoroutine(FadeChips(0f, 0.8f, 1f));

        // 4. 第三阶段：15秒存活倒计时
        _aliveTimer = 0f;
        _isAnyChipHovered = false;

        // 记录气泡生成这一瞬间，Prompt 框里的文字
        string textWhenBubblesSpawned = chatPromptInput != null ? chatPromptInput.text : "";

        while (_aliveTimer < 15f)
        {
            if (!_isAnyChipHovered) _aliveTimer += Time.deltaTime;

            // =========================================================
            // 【ML 细分 1：主动拒绝 (-1.0分)】
            // 监控文本变化：只要文本变了，说明被试者在自己打字！
            // =========================================================
            if (chatPromptInput != null && chatPromptInput.isFocused)
            {
                if (chatPromptInput.text != textWhenBubblesSpawned)
                {
                    Debug.Log("<color=red>[ML Tracker]</color> 用户在气泡存活期无视建议并自己输入了新内容，视为【主动拒绝】！记录 Explicit_Reject (-1.0分)");

                    if (InterventionTracker.Instance != null)
                    {
                        InterventionTracker.Instance.OnInterventionRejected("chat_socratic_chip");
                    }

                    ClearAllChips();
                    yield break; // 彻底了结协程
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
        if (_currentLifecycleCoroutine != null) StopCoroutine(_currentLifecycleCoroutine);
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