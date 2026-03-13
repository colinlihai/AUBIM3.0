using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// 负责处理底部主动介入 Prompt 输入框的交互、呼吸特效与意图打分
/// </summary>
public class ArticlePromptController : MonoBehaviour
{
    public string CurrentPromptInterventionType { get; private set; } = "";
    public bool IsPromptModifiedByUser { get; private set; } = false;

    private Coroutine _promptBreathCoroutine;
    private Color _originalPromptColor = Color.white;
    private string _originalAIPromptText = "";

    void Start()
    {
        // 自动绑定 UI 事件
        var promptInput = ArticleGenerator.Instance.articlePromptInput;
        if (promptInput != null)
        {
            promptInput.onSelect.AddListener(OnPromptSelected);
            promptInput.onValueChanged.AddListener(OnPromptValueChanged);
            promptInput.onEndEdit.AddListener(OnPromptBlur);
        }
    }

    // ==========================================
    // 外部调用接口
    // ==========================================
    public void StartPromptBreathing(string interventionType, string suggestedText, float duration = 60f)
    {
        var input = ArticleGenerator.Instance.articlePromptInput;
        if (input == null) return;

        CurrentPromptInterventionType = interventionType;
        _originalAIPromptText = suggestedText;
        IsPromptModifiedByUser = false;
        input.text = suggestedText;

        if (input.image != null) _originalPromptColor = input.image.color;

        if (_promptBreathCoroutine != null) StopCoroutine(_promptBreathCoroutine);
        _promptBreathCoroutine = StartCoroutine(PromptBreathingRoutine(duration));
    }

    public void ClearAndStopBreathing()
    {
        if (_promptBreathCoroutine != null) StopCoroutine(_promptBreathCoroutine);

        var input = ArticleGenerator.Instance.articlePromptInput;
        if (input != null)
        {
            if (input.image != null) input.image.color = _originalPromptColor;
            input.text = "";
        }
        CurrentPromptInterventionType = "";
        IsPromptModifiedByUser = false;
        _originalAIPromptText = "";
    }

    /// <summary>
    /// 当用户点击“生成”按钮时，结算当前 Prompt 的意图分值
    /// </summary>
    public void SettlePromptIntention()
    {
        if (_promptBreathCoroutine != null) StopCoroutine(_promptBreathCoroutine);

        var input = ArticleGenerator.Instance.articlePromptInput;
        if (input != null && input.image != null) input.image.color = _originalPromptColor;

        if (InterventionTracker.Instance != null && !string.IsNullOrEmpty(CurrentPromptInterventionType))
        {
            if (IsPromptModifiedByUser)
                InterventionTracker.Instance.OnCoCreationAccepted(CurrentPromptInterventionType);
            else
                InterventionTracker.Instance.OnButtonClicked(CurrentPromptInterventionType);
        }
        CurrentPromptInterventionType = "";
    }

    // ==========================================
    // 内部生命周期与交互逻辑
    // ==========================================
    private IEnumerator PromptBreathingRoutine(float duration)
    {
        float timer = 0f;
        Color glowColor = new Color(1f, 0.9f, 0.6f, 1f);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            var input = ArticleGenerator.Instance.articlePromptInput;

            if (input != null && input.image != null)
            {
                if (timer < 3f)
                {
                    float wave = (Mathf.Sin(Time.time * 6f) + 1f) / 2f;
                    input.image.color = Color.Lerp(_originalPromptColor, glowColor, wave);
                }
                else
                {
                    input.image.color = _originalPromptColor;
                }
            }
            yield return null;
        }

        Debug.Log($"<color=yellow>[UI]</color> Prompt 存活期 {duration}s 超时！记作无效触达(搁置)。");
        if (InterventionTracker.Instance != null) InterventionTracker.Instance.OnInterventionIgnored(CurrentPromptInterventionType);
        ClearAndStopBreathing();
    }

    private void OnPromptSelected(string text)
    {
        if (!string.IsNullOrEmpty(CurrentPromptInterventionType) && text == _originalAIPromptText)
        {
            StartCoroutine(SelectAllNextFrame());
        }
    }

    private IEnumerator SelectAllNextFrame()
    {
        yield return null;
        var input = ArticleGenerator.Instance.articlePromptInput;
        if (input != null)
        {
            input.MoveTextStart(false);
            input.MoveTextEnd(true);
        }
    }

    private void OnPromptValueChanged(string newText)
    {
        if (string.IsNullOrEmpty(CurrentPromptInterventionType)) return;
        if (newText != _originalAIPromptText) IsPromptModifiedByUser = true;
    }

    private void OnPromptBlur(string text)
    {
        if (string.IsNullOrEmpty(CurrentPromptInterventionType)) return;

        if (string.IsNullOrEmpty(text))
        {
            Debug.Log($"<color=red>[UI]</color> 用户清空 Prompt 并失焦，判定：显性拒绝！");
            if (InterventionTracker.Instance != null) InterventionTracker.Instance.OnInterventionRejected(CurrentPromptInterventionType);
            ClearAndStopBreathing();
        }
        else if (IsPromptModifiedByUser)
        {
            Debug.Log($"<color=cyan>[UI]</color> 用户修改了 Prompt 但未执行，Pending 状态。");
        }
    }
}