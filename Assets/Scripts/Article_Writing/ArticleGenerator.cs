using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;
using System.IO;
using System;
using System.Collections;

// 移除了 ArticlePromptController 的依赖
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

    [Header("UI - 纯净正文区 (全屏独占)")]
    public TMP_InputField mainBodyInput;

    [HideInInspector]
    public int lastKnownCaretPosition = 0;

    // 缓存的光标选区状态，供外部 Copilot 读取
    [HideInInspector] public int cachedSelectionStart = 0;
    [HideInInspector] public int cachedSelectionEnd = 0;

    // 拖拽节点到正文区时的事件广播，将由 Copilot 中枢接管处理
    public event Action<int, string, string, string> OnNodeDroppedEvent;

    private ArticleTextObserver _textObserver;
    private RectTransform _dropCaret; // 拖拽悬停的视觉反馈锚点

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        _textObserver = GetComponent<ArticleTextObserver>();
    }

    void Start()
    {
        if (openBtn != null) openBtn.onClick.AddListener(OnOpenModal);
        if (closeBtn != null) closeBtn.onClick.AddListener(CloseModal);
        if (exportBtn != null) exportBtn.onClick.AddListener(ExportToTxt);

        if (articleModal != null) articleModal.SetActive(false);
    }

    void Update()
    {
        if (articleModal.activeSelf && mainBodyInput != null && mainBodyInput.isFocused)
        {
            // 实时记录光标和选区，供右侧的 Copilot 实时监听
            lastKnownCaretPosition = mainBodyInput.selectionFocusPosition;
            cachedSelectionStart = Mathf.Min(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
            cachedSelectionEnd = Mathf.Max(mainBodyInput.selectionAnchorPosition, mainBodyInput.selectionFocusPosition);
        }
    }

    // ==========================================
    // 供 Copilot 调用的【纯净文本操作 API】
    // ==========================================

    /// <summary>
    /// 获取当前选中的文本
    /// </summary>
    public string GetSelectedText()
    {
        if (mainBodyInput == null || cachedSelectionEnd <= cachedSelectionStart) return "";
        return mainBodyInput.text.Substring(cachedSelectionStart, cachedSelectionEnd - cachedSelectionStart);
    }

    /// <summary>
    /// 用新文本直接替换当前选中的内容 (用于局部润色的一键替换)
    /// </summary>
    public void ReplaceSelectedText(string newText)
    {
        if (mainBodyInput == null || cachedSelectionEnd <= cachedSelectionStart) return;
        string fullText = mainBodyInput.text;
        string before = fullText.Substring(0, cachedSelectionStart);
        string after = fullText.Substring(cachedSelectionEnd);

        mainBodyInput.text = before + newText + after;

        // 替换后清空选区
        cachedSelectionStart = 0;
        cachedSelectionEnd = 0;
    }

    /// <summary>
    /// 在当前光标位置插入文本 (用于节点生成、局部续写的插入)
    /// </summary>
    public void InsertTextAtCaret(string newText)
    {
        if (mainBodyInput == null) return;
        int insertPos = Mathf.Clamp(lastKnownCaretPosition, 0, mainBodyInput.text.Length);
        mainBodyInput.text = mainBodyInput.text.Insert(insertPos, newText);
    }

    /// <summary>
    /// 将文本追加到文章最末尾 (用于顺势续写的追加)
    /// </summary>
    public void AppendTextToEnd(string newText)
    {
        if (mainBodyInput == null) return;
        mainBodyInput.text += "\n" + newText;
        StartCoroutine(ScrollToBottom(mainBodyInput));
    }

    /// <summary>
    /// 恢复历史草稿数据
    /// </summary>
    public void RestoreArticleData(string draftText)
    {
        if (mainBodyInput != null)
        {
            mainBodyInput.SetTextWithoutNotify(draftText ?? "");
            if (_textObserver != null) _textObserver.SyncHistoricalText(draftText);
        }
    }

    // ==========================================
    // 拖拽生成交互 (只保留视觉反馈与事件派发，剥离大模型)
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

        // 仅抛出事件，具体的 Prompt 组装和 LLM 请求交由后续的 Copilot 控制器处理
        OnNodeDroppedEvent?.Invoke(insertIndex, node.Data.Title, node.Data.Content, node.NodeID);
    }

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
            caretPos = textComp.textInfo.characterInfo[insertIndex].bottomLeft;
        else
            caretPos = textComp.textInfo.characterInfo[textComp.textInfo.characterCount - 1].bottomRight;

        _dropCaret.localPosition = caretPos;
    }

    public void ClearDragDropFeedback()
    {
        if (_dropCaret != null) _dropCaret.gameObject.SetActive(false);
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
                targetFolder = Path.Combine(ExperimentManager.GetUserFolderPath(), "article");
            else
                targetFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AUBIM_文章导出");

            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

            string projectName = "未命名项目";
            if (SaveSystem.Instance != null && !string.IsNullOrWhiteSpace(SaveSystem.Instance.currentSaveName))
                projectName = SaveSystem.Instance.currentSaveName;

            foreach (char c in Path.GetInvalidFileNameChars()) projectName = projectName.Replace(c.ToString(), "");

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

    public void OnOpenModal()
    {
        if (articleModal != null)
        {
            if (!articleModal.activeSelf)
            {
                articleModal.SetActive(true);
            }
            else
            {
                RectTransform rect = articleModal.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchoredPosition = Vector2.zero;
                    if (ToastSystem.Instance != null) ToastSystem.Instance.Show("成文区已复位到屏幕中央");
                }
            }
        }
    }

    public void CloseModal()
    {
        if (articleModal != null) articleModal.SetActive(false);
    }

    public IEnumerator ScrollToBottom(TMP_InputField inputField)
    {
        yield return null;
        yield return null;

        if (inputField != null)
        {
            inputField.ForceLabelUpdate();
            Canvas.ForceUpdateCanvases();
            if (inputField.verticalScrollbar != null)
            {
                if (inputField.verticalScrollbar.size < 0.99f) inputField.verticalScrollbar.value = 1f;
                else inputField.verticalScrollbar.value = 0f;
            }
        }
    }
}