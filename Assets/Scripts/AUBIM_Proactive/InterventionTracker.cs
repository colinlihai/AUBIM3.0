using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

[Serializable]
public class MLDataPoint
{
    public string InterventionType;
    public string ContextArea;
    public int CanvasNodeCount;
    public int SelectedNodeCount;
    public string LastArticleAction;

    public float RewardScore;

    // 认知生物标记：记录当时的系统容忍度偏移量，用于分析用户的深度专注状态
    public float ToleranceOffset;

    public string Timestamp;
    public string EndReason;
}

public class InterventionTracker : MonoBehaviour
{
    public static InterventionTracker Instance;

    [Header("UI 联动")]
    [Tooltip("请将场景中的三个反问/追问/解释按钮拖入此列表")]
    public List<ProactiveButtonUI> proactiveButtons;

    [Header("观察期设置 (AUBIM 自适应双轨制)")]
    public float baseCanvasStall = 20f;
    public float baseArticleStall = 15f;
    public float observationWindow = 45f;

    [Header("动态容忍度机制 (Co-adaptive)")]
    [Tooltip("当前用户的打扰容忍度偏移量 (被无视/拒绝会增加)")]
    public float currentToleranceOffset = 0f;
    public float maxToleranceOffset = 30f; // 放宽最大退让时间，防止用户极度烦躁

    public string CurrentArticleAction { get; private set; } = "None";

    [Header("跨区意图锁")]
    public bool isSuspendedByChat = false;

    public bool isAIProcessing = false;   // 记录 AI 是否正在请求网络
    private float _readingBuffer = 0f;    // 阅读免打扰倒计时

    // 供外部 AI 生成器呼叫
    public void SetAIProcessing(bool isProcessing)
    {
        isAIProcessing = isProcessing;
        if (isProcessing) _idleTimer = 0f; // 一旦开始生成，发呆计时彻底清零
    }

    // 供外部按字数分配阅读时间
    public void GrantReadingBuffer(int charCount)
    {
        // 假设人类阅读中文的舒适速度约为 10字/秒
        // 给予等比例的缓冲时间，最高不超过 120 秒（2分钟）
        _readingBuffer = Mathf.Clamp(charCount * 0.1f, 0f, 120f);
        _idleTimer = 0f;
        Debug.Log($"<color=cyan>[Tracker]</color> AI 输出了 {charCount} 字，进入阅读免打扰模式，静默倒计时延长 {_readingBuffer:F1} 秒。");
    }

    // ==========================================
    // 精准意图锁控制方法
    // ==========================================
    public void SuspendByChat()
    {
        if (!isSuspendedByChat)
        {
            isSuspendedByChat = true;
            _idleTimer = 0f; // 重置工作区静默计时
            Debug.Log("<color=orange>[Tracker 状态锁]</color> 侦测到聊天区焦点，主动介入已锁定。");
        }
    }

    public void ResumeFromChat()
    {
        if (isSuspendedByChat)
        {
            isSuspendedByChat = false;
            _idleTimer = 0f; // 解锁后，重新开始倒计时发呆
            Debug.Log("<color=green>[Tracker 状态锁]</color> 侦测到工作区主焦点，主动介入已解锁！");
        }
    }

    private float _idleTimer = 0f;
    private bool _isBreathingActive = false;
    private string _lastPredictedType = "";

    private bool _isObserving = false;
    private float _observationTimer = 0f;
    private MLDataPoint _currentDataPoint;
    private string _trackedTargetID;

    private string DataFolderPath => ExperimentManager.GetUserFolderPath();
    private string MLDataPath => Path.Combine(DataFolderPath, "ML_TrainingDataset.json");

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    void OnEnable() { UserBehaviorSystem.OnEventLogged += HandleIncomingLog; }
    void OnDisable() { UserBehaviorSystem.OnEventLogged -= HandleIncomingLog; }

    void Update()
    {
        // 模块 1：自适应静默检测器 (Adaptive Stall Timer)
        if (!_isBreathingActive && !_isObserving && !isSuspendedByChat)
        {
            // =========================================================
            // 【防御 1】：AI 正在请求网络生成内容，彻底锁死计时器
            // =========================================================
            if (isAIProcessing)
            {
                _idleTimer = 0f;
                return; // 直接退出，不在等待期算发呆
            }

            bool isUsingIME = !string.IsNullOrEmpty(Input.compositionString);
            bool hasTextInput = !string.IsNullOrEmpty(Input.inputString);

            // =========================================================
            // 【防御 2】：用户主动打字 -> 立刻取消阅读缓冲，发呆清零
            // =========================================================
            if (Input.anyKey || isUsingIME || hasTextInput)
            {
                _idleTimer = 0f;
                _readingBuffer = 0f; // 用户敲击键盘，说明进入了主动输出状态，彻底结束阅读保护
            }
            // =========================================================
            // 【防御 3】：纯鼠标操作 (精确区分“点击”和“滚动”)
            // =========================================================
            else if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || Mathf.Abs(Input.mouseScrollDelta.y) > 0.1f)
            {
                _idleTimer = 0f;

                // 核心修复：如果是鼠标“点击”（比如点在了正文区、Prompt区，改变了光标位置），也视为交互意图，结束阅读保护
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
                {
                    _readingBuffer = 0f;
                }
                // 注意：如果只是单独滚动滚轮 (mouseScrollDelta)，代码不会进入这里，_readingBuffer 保持不变，允许用户安心看完长文
            }
            // =========================================================
            // 【防御 4】：双手离开键盘鼠标 -> 先消耗阅读缓冲，再算发呆
            // =========================================================
            else
            {
                if (_readingBuffer > 0)
                {
                    _readingBuffer -= Time.deltaTime; // 消耗阅读免打扰时间
                    _idleTimer = 0f;                  // 发呆计时器死死按在 0
                }
                else
                {
                    // 缓冲期结束（或被点击打断后），一旦停手，正式开始算发呆
                    _idleTimer += Time.deltaTime;
                    bool isArticleActive = ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy;
                    float currentThreshold = (isArticleActive ? baseArticleStall : baseCanvasStall) + currentToleranceOffset;

                    if (_idleTimer >= currentThreshold)
                    {
                        TriggerProactiveBreathing();
                    }
                }
            }
        }

        // 模块 2：60秒存活观察期 (Survival Timer)
        if (_isObserving)
        {
            _observationTimer += Time.deltaTime;
            if (_observationTimer >= observationWindow)
            {
                ConcludeObservation(0f, "Timeout_Ignored");
            }
        }
    }

    private void TriggerProactiveBreathing()
    {
        if (InterventionClassifier.Instance == null) return;

        _isBreathingActive = true;
        _idleTimer = 0f;

        bool isArticleActive = ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy;
        int totalNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetAllNodes().Count : 0;
        int selectedNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetSelectedNodes().Count : 0;

        InterventionType bestType = InterventionType.None;
        float maxProb = -1f;

        InterventionType[] candidateTypes = isArticleActive ?
            new InterventionType[] { InterventionType.ArticleGap, InterventionType.ArticleReflect } :
            new InterventionType[] { InterventionType.Socratic, InterventionType.Counter, InterventionType.Elaborate };
        string contextStr = isArticleActive ? "Article" : "Canvas";

        foreach (var t in candidateTypes)
        {
            float prob = InterventionClassifier.Instance.PredictAcceptanceProbability(t.ToString(), contextStr, totalNodes, selectedNodes, CurrentArticleAction);
            if (prob > maxProb)
            {
                maxProb = prob;
                bestType = t;
            }
        }

        _lastPredictedType = bestType.ToString();
        Debug.Log($"<color=cyan>[Tracker]</color> 触发介入！区域: {contextStr} | 预测: {bestType} | 当前容忍度偏移: +{currentToleranceOffset}s");

        if (isArticleActive)
        {
            _isBreathingActive = false;
            if (ProactiveInterventionSystem.Instance != null)
                ProactiveInterventionSystem.Instance.TriggerInterventionByType(bestType);
        }
        else // 【画布区逻辑】
        {
            if (selectedNodes > 0)
            {
                // 分支 A：用户选中了节点发呆 -> 闪烁对应的跟随按钮
                if (proactiveButtons != null)
                {
                    foreach (var btn in proactiveButtons)
                    {
                        if (btn.interventionType == bestType)
                        {
                            if (btn.gameObject.activeInHierarchy)
                            {
                                btn.StartAIBreathing(20f);
                            }
                            else
                            {
                                Debug.LogWarning($"[Tracker] 试图呼叫 {bestType} 按钮呼吸，但它处于隐藏状态。");
                            }
                            break;
                        }
                    }
                }
            }
            else
            {
                // ==================================================
                // 分支 B：【核心修复】纯白板/全局发呆 -> 直接空投全局节点
                // ==================================================
                Debug.Log($"<color=cyan>[Tracker 全局介入]</color> 用户全局静默，直接空投 {bestType} 节点！");

                if (GlobalProactiveButton.Instance != null)
                {
                    GlobalProactiveButton.Instance.StartGlobalBreathing(bestType);
                }
            }
        }
    }

    // =========================================================
    // 【终极闭环：5 级反馈调节矩阵】
    // =========================================================

    /// <summary>
    /// 等级 1：显性拒绝 (强负向 -1.0) - 用户主动删除了提示
    /// </summary>
    public void OnInterventionRejected(string type)
    {
        _isBreathingActive = false;
        _idleTimer = 0f;

        currentToleranceOffset = Mathf.Min(currentToleranceOffset + 5f, maxToleranceOffset);

        Debug.Log($"<color=red>[ML Tracker]</color> 显性拒绝 (Score: -1.0)。系统大幅退后，容忍度延长至 +{currentToleranceOffset}s。");
        RecordSingleMLData(type, -1.0f, "Explicit_Reject");
    }

    /// <summary>
    /// 等级 2：搁置 / 无效触达 (弱负向 -0.2) - 1分钟超时或意图被覆盖
    /// </summary>
    public void OnInterventionIgnored(string type)
    {
        _isBreathingActive = false;
        _idleTimer = 0f;

        // 系统略微克制
        currentToleranceOffset = Mathf.Min(currentToleranceOffset + 1f, maxToleranceOffset);

        Debug.Log($"<color=yellow>[ML Tracker]</color> 无视/搁置 (Score: -0.2)。系统小幅退后，容忍度延长至 +{currentToleranceOffset}s。");
        RecordSingleMLData(type, -0.2f, "Ignored_Timeout_Or_Overwritten");
    }

    /// <summary>
    /// 等级 3：隐性采纳 (弱正向 +0.5) - 提示激发了用户自主打字
    /// </summary>
    public void OnImplicitScaffoldAccepted(string type)
    {
        _isBreathingActive = false;
        _idleTimer = 0f;

        // 表现不错，逐渐恢复自信
        currentToleranceOffset = Mathf.Max(currentToleranceOffset - 1.5f, 0f);

        Debug.Log($"<color=magenta>[ML Tracker]</color> 隐性支架采纳 (Score: +0.5)！容忍度回缩至 +{currentToleranceOffset}s。");
        RecordSingleMLData(type, 0.5f, "Implicit_Scaffold_Accepted");
        _lastPredictedType = "";
    }

    /// <summary>
    /// 等级 4：显性采纳 (强正向 +1.0) - 原封不动点击生成
    /// </summary>
    public void OnButtonClicked(string clickedType)
    {
        _isBreathingActive = false;
        _idleTimer = 0f;

        // 击中需求，信心大增
        currentToleranceOffset = Mathf.Max(currentToleranceOffset - 3f, 0f);

        Debug.Log($"<color=green>[ML Tracker]</color> 显性采纳 (Score: +1.0)！容忍度回缩至 +{currentToleranceOffset}s。");
        RecordSingleMLData(clickedType, 1.0f, "Explicit_Adopt");
        _lastPredictedType = "";
    }

    /// <summary>
    /// 等级 5：协作共创 (超强正向 +1.5) - 修改 AI 提示后生成
    /// </summary>
    public void OnCoCreationAccepted(string type)
    {
        _isBreathingActive = false;
        _idleTimer = 0f;

        // 完美互动，彻底信任
        currentToleranceOffset = Mathf.Max(currentToleranceOffset - 5f, 0f);

        Debug.Log($"<color=cyan>[ML Tracker]</color> 深度协作共创 (Score: +1.5)！极高价值互动，容忍度回缩至 +{currentToleranceOffset}s。");
        RecordSingleMLData(type, 1.5f, "Co_Creation");
        _lastPredictedType = "";
    }


    // =========================================================
    // 数据收集与记录 (画布区逻辑保持平滑过渡)
    // =========================================================
    private void HandleIncomingLog(TelemetryLog log)
    {
        string eType = log.EventType;
        _idleTimer = 0f;

        // 只要接收到来自 Canvas(导图区)、Article(成文区) 或 Node(节点) 的交互日志，立刻砸碎聊天锁！
        if (isSuspendedByChat && (eType.StartsWith("Canvas_") || eType.StartsWith("Article_") || eType.StartsWith("Edit_") || eType.StartsWith("Node_") || eType.StartsWith("Object_")))
        {
            ResumeFromChat();
        }

        if (eType == "Article_Generate_Global") CurrentArticleAction = "Global";
        else if (eType == "Article_Generate_Local") CurrentArticleAction = "Local";
        else if (eType == "Article_Generate_Node") CurrentArticleAction = "Node";
        else if (eType == "Edit_Article_Body") CurrentArticleAction = "None";

        if (eType == "AI_Intervention_Triggered" && !_isObserving)
        {
            StartObservation(log);
            return;
        }

        if (_isObserving)
        {
            if (_currentDataPoint.ContextArea == "Canvas")
            {
                if (eType == "Object_Delete" && log.TargetID == _trackedTargetID)
                {
                    if (_observationTimer < 12.0f)
                    {
                        Debug.Log($"<color=red>[ML Tracker]</color> AI 节点存活仅 {_observationTimer:F1}s 被秒删，判定为拒绝 (Score: -1.0)。");
                        ConcludeObservation(-1.0f, "Deleted_Instantly_Rejected");
                    }
                    else
                    {
                        Debug.Log($"<color=cyan>[ML Tracker]</color> AI 节点存活 {_observationTimer:F1}s 后被删，判定为灵感吸收 (Score: +0.5)。");
                        ConcludeObservation(0.5f, "Absorbed_And_Deleted");
                    }
                    return; // 关键修复：结束判断，防止继续往下跑！
                }
                else if (eType == "Canvas_LinkNodes" && log.ContextInfo.Contains(_trackedTargetID))
                {
                    ConcludeObservation(1.0f, "Linked_Node");
                    return;
                }
                else if ((eType == "Edit_Node_Body_End" || eType == "Edit_Node_Title_End") && log.TargetID == _trackedTargetID)
                {
                    // 用户双击修改了 AI 节点的内容，证明高度协同！
                    ConcludeObservation(1.5f, "Edited_Node");
                    return;
                }
                else if (eType == "AI_Intervention_Extended" && log.ContextInfo.Contains(_trackedTargetID))
                {
                    ConcludeObservation(1.0f, "Extended_Node");
                    return;
                }
            }
        }
    }

    private void StartObservation(TelemetryLog log)
    {
        _isObserving = true;
        _observationTimer = 0f;
        _trackedTargetID = log.TargetID;

        int totalNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetAllNodes().Count : 0;
        int selectedNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetSelectedNodes().Count : 0;

        _currentDataPoint = new MLDataPoint
        {
            InterventionType = log.ContextInfo,
            ContextArea = "Canvas",
            CanvasNodeCount = totalNodes,
            SelectedNodeCount = selectedNodes,
            LastArticleAction = CurrentArticleAction,
            ToleranceOffset = currentToleranceOffset,
            Timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss")
        };
    }

    private void ConcludeObservation(float rewardScore, string reason)
    {
        _isObserving = false;
        _currentDataPoint.RewardScore = rewardScore;
        _currentDataPoint.EndReason = reason;
        SaveDataPointToFile(_currentDataPoint);
    }

    private void RecordSingleMLData(string interventionType, float rewardScore, string reason)
    {
        int totalNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetAllNodes().Count : 0;
        int selectedNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetSelectedNodes().Count : 0;

        MLDataPoint pt = new MLDataPoint
        {
            InterventionType = interventionType,
            ContextArea = (ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy) ? "Article" : "Canvas",
            CanvasNodeCount = totalNodes,
            SelectedNodeCount = selectedNodes,
            LastArticleAction = CurrentArticleAction,
            RewardScore = rewardScore,
            EndReason = reason,
            ToleranceOffset = currentToleranceOffset,
            Timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss")
        };
        SaveDataPointToFile(pt);
    }

    private void SaveDataPointToFile(MLDataPoint data)
    {
        try
        {
            string json = JsonUtility.ToJson(data) + ",\n";
            File.AppendAllText(MLDataPath, json);
        }
        catch (Exception e)
        {
            Debug.LogError("[ML Tracker] 保存训练数据失败：" + e.Message);
        }
    }

    // 当用户进行了主动操作（如切换节点）时，强行中断局部 AI 的呼唤
    public void AbortLocalBreathing()
    {
        if (!_isBreathingActive) return;

        bool actuallyAborted = false;
        if (proactiveButtons != null)
        {
            foreach (var btn in proactiveButtons)
            {
                if (btn.isBreathing)
                {
                    btn.StopBreathingEarly();
                    actuallyAborted = true;
                }
            }
        }

        if (GlobalProactiveButton.Instance != null && GlobalProactiveButton.Instance.isBreathing)
        {
            GlobalProactiveButton.Instance.StopBreathingEarly();
            actuallyAborted = true;
        }

        if (actuallyAborted)
        {
            _isBreathingActive = false;
            _idleTimer = 0f;
            // 记录为：AI 正在提示时，被用户用其他动作（比如新建节点）打断/无视了
            OnInterventionIgnored(_lastPredictedType);
        }
    }
}