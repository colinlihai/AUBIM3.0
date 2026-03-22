using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections; // 必须引用

public class NodeController : BaseNodeController
{
    // 记录上一次 AI 介入时的正文快照
    private string _lastTitleGenSourceContent = "";
    private bool _hasGeneratedTitleOnce = false;

    [Header("UI 组件引用")]
    public TMP_InputField titleInput; // 引用 Title_Input
    public TMP_InputField bodyInput;  // 引用 CoreBody 里的 Input

    private Image _titleHitBox;
    private string _tempStartTitle;

    protected override void Awake()
    {
        base.Awake();
    }

    protected override void Start()
    {
        base.Start();

        // 这一步确保当从存档加载(Data有值)时，Input立刻显示标题
        // 避免因为 Input 默认为空，导致下面的 UpdateTitleRaycast 把交互给关了
        if (Data != null && !string.IsNullOrEmpty(Data.Title) && titleInput != null)
        {
            titleInput.text = Data.Title;
        }

        if (titleInput != null)
        {
            // 获取 InputField 依赖的图形组件 (Target Graphic)
            _titleHitBox = titleInput.GetComponent<Image>();

            // 如果 InputField 没有 Image，尝试找它的 TargetGraphic
            if (_titleHitBox == null && titleInput.targetGraphic != null)
            {
                _titleHitBox = titleInput.targetGraphic as Image;
            }

            // 初始化状态：没字就禁用，有字就启用
            UpdateTitleRaycast(titleInput.text);

            // 监听变化：只要内容变了，就重新判断
            titleInput.onValueChanged.AddListener(UpdateTitleRaycast);

            // 1. 获得焦点：记录旧标题 + 埋点
            titleInput.onSelect.AddListener((val) => {
                _tempStartTitle = titleInput.text;
            });
            // 2. 失去焦点：提交命令 + 埋点
            titleInput.onDeselect.AddListener((val) => {
                string finalTitle = titleInput.text;

                // 只有内容变了才提交命令
                if (CommandManager.Instance != null && finalTitle != _tempStartTitle)
                {
                    if (Data != null) Data.Title = finalTitle;

                    var cmd = new EditTitleCommand(this, _tempStartTitle, finalTitle);
                    CommandManager.Instance.ExecuteCommand(cmd);
                }
            });
        }

        // 1. 监听正文编辑结束：模拟 LLM 生成标题
        if (bodyInput != null)
        {
            bodyInput.lineType = TMP_InputField.LineType.MultiLineNewline;
            bodyInput.onEndEdit.AddListener(OnContentEndEdit);
        }

        if (Data != null)
        {
            // 如果加载进来时，正文已经很长且有标题，我们认为它已经是“老节点”了，视为已生成过
            // 避免加载一个旧的大节点，用户一改字，系统以为是新建的非要等20个字才反应
            if (!string.IsNullOrEmpty(Data.Title) && Data.Title != "新节点" && Data.Content.Length > 20)
            {
                _hasGeneratedTitleOnce = true;
                _lastTitleGenSourceContent = Data.Content;
            }
            else
            {
                _hasGeneratedTitleOnce = false;
                _lastTitleGenSourceContent = ""; // 重置为空，确保从零开始计数
            }
        }
    }

    // ==========================================
    // 【核心修复 2】：接管并分离 Enter 与 Shift+Enter 逻辑
    // ==========================================
    private void Update() // 去掉 protected override
    {
        // 删掉了 base.Update();

        if (bodyInput != null && bodyInput.isFocused)
        {
            // 检测到按下了回车键 (主键盘回车 或 小键盘回车)
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                // 如果没有按住 Shift 键，说明用户想“提交并失焦”
                if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                {
                    StartCoroutine(HandleSubmitAndDefocus());
                }
                // 如果按住了 Shift，什么都不做！
                // 因为 bodyInput 已经是 MultiLineNewline 模式，它原生会完美处理换行且绝不失焦
            }
        }
    }

    private IEnumerator HandleSubmitAndDefocus()
    {
        // 延迟到当前帧末尾，确保 TMP 已经把原生换行符敲进 text 里了
        yield return new WaitForEndOfFrame();

        if (bodyInput != null)
        {
            // 把刚刚因为按 Enter 产生的那一个多余的换行符切掉
            bodyInput.text = bodyInput.text.TrimEnd('\r', '\n');

            // 手动强制失焦！这会极其自然地触发上面的 onEndEdit 事件
            bodyInput.DeactivateInputField();
        }
    }

    // 当正文编辑结束 (OnEndEdit) 时调用此方法
    public void TryRefreshTitleByAI()
    {
        string currentContent = Data.Content;

        // [修改] 调用 Detector 时传入 _hasGeneratedTitleOnce
        bool shouldTrigger = AIChangeDetector.IsChangeSignificant(
            _lastTitleGenSourceContent,
            currentContent,
            _hasGeneratedTitleOnce
        );

        if (shouldTrigger)
        {
            Debug.Log($"[AI Trigger] Node {NodeID} 满足条件，开始生成标题...");

            // 1. 立即更新快照，防止在 AI 回复前用户继续打字导致重复触发
            _lastTitleGenSourceContent = currentContent;
            // 2. 标记已经触发过 (下次就进入 update 模式)
            _hasGeneratedTitleOnce = true;
            // 调用 AI
            AITaskAssistant.Instance.GenerateTitle(currentContent, (newTitle) =>
            {
                if (this == null) return;

                // [埋点] AI 自动拟题触发
                if (UserBehaviorSystem.Instance != null)
                {
                    UserBehaviorSystem.Instance.LogEvent(
                        BehaviorEventType.AI_AutoTitle_Triggered,
                        targetID: NodeID,
                        info: "AutoTitle",
                        value: 1
                    );
                }

                // 回调中执行 Command 修改
                var cmd = new EditTitleCommand(this, Data.Title, newTitle);
                CommandManager.Instance.ExecuteCommand(cmd);
            });
        }
    }

    private void UpdateTitleRaycast(string text)
    {
        if (titleInput == null) return;

        bool hasContent = !string.IsNullOrEmpty(text);

        // 1. 获取 InputField 身上及子物体里所有的图形组件
        // 这包括：Background Image, Text Component, Placeholder Component
        Graphic[] allGraphics = titleInput.GetComponentsInChildren<Graphic>();

        foreach (var g in allGraphics)
        {
            // 统统设为与内容状态一致
            // 没内容 -> 全透 (false)
            // 有内容 -> 可点 (true)
            g.raycastTarget = hasContent;
        }

        // [额外保险] 有时候 InputField 自身挂在父物体上，可能还有额外的 Image
        // 这一步确保如果 titleInput 自己身上有 Image 也能被处理
        // (GetComponentsInChildren 默认包含自身，但为了保险起见保留之前的逻辑引用)
        if (_titleHitBox != null)
        {
            _titleHitBox.raycastTarget = hasContent;
        }
    }



    private void OnContentEndEdit(string content)
    {
        if (content.Length < 30)
        {
            return;
        }

        if (titleInput != null && string.IsNullOrEmpty(titleInput.text) && !string.IsNullOrEmpty(content))
        {
            // 1. 视觉反馈
            titleInput.text = "AI生成中...";

            // 2. 调用 AI 助手
            if (AITaskAssistant.Instance != null)
            {
                AITaskAssistant.Instance.GenerateTitle(content, (aiTitle) =>
                {
                    // 异步回调：更新 UI 和 数据
                    // 检查 this 是否还存在 (防止节点被删)
                    if (this != null && titleInput != null)
                    {
                        titleInput.text = aiTitle;
                        if (Data != null) Data.Title = aiTitle;

                        Debug.Log($"[AI] 已为节点 {NodeID} 生成标题: {aiTitle}");
                    }
                });
            }
            else
            {
                Debug.Log("jiangji");
                // 降级方案：如果没有 AI 助手，还是截取
                string summary = content.Length > 10 ? content.Substring(0, 10) : content;
                titleInput.text = summary;
            }
        }
    }

    private void OnTitleEndEdit(string content)
    {
        if (Data != null) Data.Title = content;
    }

    public void RestoreSize(float savedWidth)
    {
        // 1. 尝试在 Visual (CoreBody) 上找 LayoutElement
        LayoutElement le = null;

        if (visual != null)
        {
            le = visual.GetComponent<LayoutElement>();
        }

        // 2. 如果 Visual 上没找到，尝试在自己身上找 (兼容性兜底)
        if (le == null)
        {
            le = GetComponent<LayoutElement>();
        }

        // 3. 应用宽度
        if (le != null)
        {
            // 你的 CoreBody 宽度是由 preferredWidth 控制的
            le.preferredWidth = savedWidth;
            var autoComp = le.GetComponent<AutoWidthByContent>();
            if (autoComp != null)
            {
                autoComp.isAutoWidth = false; // <--- 关键！锁定为手动模式
            }
            // 强制触发布局刷新，防止加载瞬间闪烁或重叠
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
        }
    }

    /// <summary>
    /// 强制让正文输入框获得焦点 (用于新建节点时自动进入编辑模式)
    /// </summary>
    public void FocusBodyInput()
    {
        // 开启协程，等待 UI 初始化完成
        StartCoroutine(FocusRoutine());
    }

    private System.Collections.IEnumerator FocusRoutine()
    {
        // [关键] 必须等待一帧！
        // 否则刚生成的 InputField 可能还没注册到 EventSystem，导致聚焦失败
        yield return null;

        if (visual != null)
        {
            var visualScript = visual.GetComponent<CoreBodyVisual>();
            if (visualScript != null)
            {
                // [核心] 调用刚才公开的方法
                visualScript.EnterEditMode();

                // 额外优化：把光标移到末尾 (如果你不想全选的话)
                if (bodyInput != null)
                {
                    bodyInput.MoveTextEnd(false);
                }
            }
        }
    }
}