using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

// ==========================================
// 1. 结构升级：用于 ML 的单次交互数据点
// ==========================================
[Serializable]
public class MLDataPoint
{
    public string InterventionType;
    public string ContextArea;
    public int CanvasNodeCount;
    public int SelectedNodeCount;
    public string LastArticleAction;

    public float RewardScore;
    public float ToleranceOffset; // 记录发生时的偏移量
    public string Timestamp;
    public string EndReason;
}

// ==========================================
// 2. 结构升级：持久化的用户认知印记 (User Profile)
// ==========================================
[Serializable]
public class InterventionRecord
{
    public string categoryName;   // 类别：如 Socratic, Elaborate, Canvas_Global, Article_Local 等
    public float toleranceOffset; // 专属的容忍度退让时间 (0 到 30秒)
    public float totalScore;      // 历史总得分
    public int triggerCount;      // 触发次数
}

[Serializable]
public class UserProfileData
{
    public string subjectID;
    public List<InterventionRecord> actionRecords = new List<InterventionRecord>();
}

public class InterventionTracker : MonoBehaviour
{
    public static InterventionTracker Instance;

    [Header("UI 联动")]
    public List<ProactiveButtonUI> proactiveButtons;

    [Header("观察期设置 (AUBIM 自适应双轨制)")]
    public float baseCanvasStall = 35f;
    public float baseArticleStall = 25f;
    public float observationWindow = 45f;
    public float maxToleranceOffset = 30f;

    [Header("ML 容忍度步长调节 (动态颗粒度)")]
    public float penaltyExplicitReject = 10f; // 显性拒绝增加的容忍度 (+10s)
    public float penaltyIgnored = 3f;         // 搁置无视增加的容忍度 (+3s)
    public float rewardImplicit = -1.5f;      // 隐性采纳减少的容忍度 (-1.5s)
    public float rewardExplicit = -3f;        // 显性采纳减少的容忍度 (-3s)
    public float rewardCoCreation = -5f;      // 深度共创减少的容忍度 (-5s)
    public float rewardPreemptive = -1f;      // 提前抢答减少的容忍度 (-1s)

    public string CurrentArticleAction { get; private set; } = "None";

    [Header("状态锁")]
    public bool isSuspendedByChat = false;
    public bool isAIProcessing = false;
    private float _readingBuffer = 0f;
    private bool _isAwaitingUserAction = false;

    private float _idleTimer = 0f;
    private bool _isBreathingActive = false;
    private float _currentBreathingDuration = 0f;
    private string _lastPredictedRecordKey = ""; // 记录当前正在闪烁的专属类型 Key

    private bool _isObserving = false;
    private float _observationTimer = 0f;
    private MLDataPoint _currentDataPoint;
    private string _trackedTargetID;

    // 持久化档案
    private UserProfileData _userProfile;
    private string DataFolderPath => ExperimentManager.GetUserFolderPath();
    private string MLDataPath => Path.Combine(DataFolderPath, "ML_TrainingDataset.json");
    private string UserProfilePath => Path.Combine(DataFolderPath, "User_CognitiveProfile.json");

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    void Start()
    {
        LoadUserProfile();
    }

    void OnEnable() { UserBehaviorSystem.OnEventLogged += HandleIncomingLog; }
    void OnDisable() { UserBehaviorSystem.OnEventLogged -= HandleIncomingLog; }

    // ==========================================
    // 供外部呼叫的锁机制
    // ==========================================
    public void SetAIProcessing(bool isProcessing)
    {
        isAIProcessing = isProcessing;
        if (isProcessing) _idleTimer = 0f;
    }

    public void GrantReadingBuffer(int charCount)
    {
        float baseTime = charCount <= 100 ? charCount * 0.1f : 10f + (charCount - 100) * 0.03f;

        // 获取当前被试者针对聊天逼问气泡的专属容忍度
        float tolerance = GetToleranceOffset("chat_socratic_chip");

        // 最终护盾 = 基础阅读 + 容忍度，且硬性封顶最高不超过 50 秒
        _readingBuffer = Mathf.Clamp(baseTime + tolerance, 0f, 50f);
        _idleTimer = 0f;
        Debug.Log($"<color=cyan>[Tracker]</color> AI 输出了 {charCount} 字，阅读免打扰护盾启动：{_readingBuffer:F1} 秒。");
    }

    public void SuspendByChat()
    {
        if (!isSuspendedByChat)
        {
            isSuspendedByChat = true;
            _idleTimer = 0f;
        }
    }

    public void ResumeFromChat()
    {
        if (isSuspendedByChat)
        {
            isSuspendedByChat = false;
            _idleTimer = 0f;
        }
    }

    void Update()
    {
        // 捕获所有物理输入
        bool isUsingIME = !string.IsNullOrEmpty(Input.compositionString);
        bool hasTextInput = !string.IsNullOrEmpty(Input.inputString);
        bool hasMouseAction = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || Mathf.Abs(Input.mouseScrollDelta.y) > 0.1f;
        bool hasAnyInput = Input.anyKey || isUsingIME || hasTextInput || hasMouseAction;

        if (_isBreathingActive)
        {
            _currentBreathingDuration += Time.deltaTime;

            bool isPhysicallyBreathing = false;

            if (proactiveButtons != null)
            {
                foreach (var btn in proactiveButtons)
                {
                    if (btn != null && btn.gameObject.activeInHierarchy && btn.isBreathing)
                    {
                        isPhysicallyBreathing = true;
                        break;
                    }
                }
            }

            if (!isPhysicallyBreathing && GlobalProactiveButton.Instance != null)
            {
                if (GlobalProactiveButton.Instance.gameObject.activeInHierarchy && GlobalProactiveButton.Instance.isBreathing)
                {
                    isPhysicallyBreathing = true;
                }
            }

            if (!isPhysicallyBreathing && CopilotActionController.Instance != null)
            {
                if (CopilotActionController.Instance.IsAnyButtonGlowing)
                {
                    isPhysicallyBreathing = true;
                }
            }

            if (!isPhysicallyBreathing)
            {
                Debug.Log("<color=yellow>[Tracker 守卫]</color> 物理 UI 已消失，清理静默残留并结算为搁置！");
                AbortLocalBreathing();
                return;
            }

            if (hasTextInput || isUsingIME || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Backspace))
            {
                Debug.Log("<color=yellow>[Tracker 守卫]</color> 侦测到键盘输入，强行打断当前 AI 闪烁！");
                AbortLocalBreathing();
                return;
            }
        }

        // 模块 1：自适应静默检测器 
        if (!_isBreathingActive && !_isObserving && !isSuspendedByChat)
        {
            if (isAIProcessing)
            {
                _idleTimer = 0f;
                return;
            }

            // 无论系统处于什么锁死状态，只要检测到用户在操作键鼠，立刻重置并解锁！
            if (hasAnyInput)
            {
                if (_isAwaitingUserAction)
                {
                    Debug.Log("<color=green>[Tracker 解锁]</color> 侦测到物理输入动作，解除防挂机锁！");
                    _isAwaitingUserAction = false;
                }

                _idleTimer = 0f;

                // 阅读护盾仅在点击或按键时破坏，单纯滚轮不破坏
                if (Input.anyKey || isUsingIME || hasTextInput || Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
                {
                    _readingBuffer = 0f;
                }

                return; // 跳过本帧的发呆累加
            }

            // 如果处于防挂机锁定状态，不再继续累加发呆时间
            if (_isAwaitingUserAction)
            {
                _idleTimer = 0f;
                return;
            }

            if (_readingBuffer > 0)
            {
                _readingBuffer -= Time.deltaTime;
                _idleTimer = 0f;
            }
            else
            {
                bool isArticleActive = ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy;
                int totalNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetAllNodes().Count : 0;
                int selectedNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetSelectedNodes().Count : 0;
                if (!isArticleActive && selectedNodes == 0 && totalNodes <= 5)
                {
                    _idleTimer = 0f;
                    return;
                }

                _idleTimer += Time.deltaTime;

                InterventionType bestType = GetBestPredictedType(out string contextStr, out bool isGlobal, out string recordKey);

                float specificOffset = GetToleranceOffset(recordKey);
                float currentThreshold = (contextStr == "Article" ? baseArticleStall : baseCanvasStall) + specificOffset;

                if (_idleTimer >= currentThreshold)
                {
                    TriggerProactiveBreathing(bestType, recordKey, contextStr, isGlobal);
                }
            }
        }

        // 模块 2：存活观察期 
        if (_isObserving)
        {
            _observationTimer += Time.deltaTime;
            if (_observationTimer >= observationWindow)
            {
                ConcludeObservation(-0.2f, "Timeout_Ignored");
            }
        }
    }

    // ==========================================
    // 预测与触发逻辑：完美实现 9 大 Button 的严格隔离与映射
    // ==========================================
    private InterventionType GetBestPredictedType(out string contextStr, out bool isGlobal, out string exactMLKey)
    {
        // 核心路由隔离：以 ArticleModal 的状态为绝对权威
        bool isArticleActive = ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy;

        int totalNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetAllNodes().Count : 0;
        int selectedNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetSelectedNodes().Count : 0;

        contextStr = isArticleActive ? "Article" : "Canvas";
        isGlobal = (!isArticleActive && selectedNodes == 0);
        exactMLKey = "";
        InterventionType bestType = InterventionType.None;

        if (InterventionClassifier.Instance == null) return bestType;

        float maxProb = -1f;

        // =========================================================
        // 路由 A：成文区 5 大按钮触发逻辑
        // =========================================================
        if (isArticleActive)
        {
            var input = ArticleGenerator.Instance.mainBodyInput;
            string text = input.text;
            bool isFocused = input.isFocused;
            int cursorIndex = input.selectionFocusPosition;

            // 判断用户是否有高亮选中文本
            int selStart = Mathf.Min(input.selectionAnchorPosition, input.selectionFocusPosition);
            int selEnd = Mathf.Max(input.selectionAnchorPosition, input.selectionFocusPosition);
            bool hasSelection = selEnd > selStart;

            if (string.IsNullOrWhiteSpace(text))
            {
                // 1. 全文起草 (空白)
                exactMLKey = "article_coldstart";
                bestType = InterventionType.ArticleDraft;
            }
            else if (hasSelection)
            {
                // 2. 局部润色 (有选中高亮文字)
                exactMLKey = "article_refine";
                bestType = InterventionType.ArticleRefine;
            }
            else if (!isFocused)
            {
                // 3. 全局审稿 (没选中字，且失去焦点没在打字)
                exactMLKey = "article_reflect";
                bestType = InterventionType.ArticleReview;
            }
            else if (text.Length - cursorIndex <= 15)
            {
                // 4. 顺势续写 (光标在末尾)
                exactMLKey = "article_expand";
                bestType = InterventionType.ArticleExpand;
            }
            else
            {
                // 5. 内容衔接 (光标在中间)
                exactMLKey = "article_stitch";
                bestType = InterventionType.ArticleStitch;
            }

            // 获取预测概率 (成文区只取命中条件的唯一目标)
            maxProb = InterventionClassifier.Instance.PredictAcceptanceProbability(exactMLKey, contextStr, totalNodes, selectedNodes, CurrentArticleAction, "");
        }
        // =========================================================
        // 路由 B：画布区 4 大按钮触发逻辑
        // =========================================================
        else
        {
            string targetContent = selectedNodes > 0 && NodeCardManager.Instance.GetSelectedNodes()[0].Data != null
                                   ? NodeCardManager.Instance.GetSelectedNodes()[0].Data.Content : "";

            if (isGlobal && totalNodes > 5)
            {
                // 6. 全局思考 (未选中且节点数>5)
                exactMLKey = "proactive_global";
                bestType = InterventionType.GlobalInsight;
            }
            else if (selectedNodes == 1)
            {
                // 7/8/9. 局部反问/追问/解释 (让机器学习大脑算一下哪个最合适)
                string[] canvasCandidates = new string[] { "proactive_socratic", "proactive_counter", "proactive_elaborate" };
                InterventionType[] canvasTypes = new InterventionType[] { InterventionType.Socratic, InterventionType.Counter, InterventionType.Elaborate };

                for (int i = 0; i < canvasCandidates.Length; i++)
                {
                    float prob = InterventionClassifier.Instance.PredictAcceptanceProbability(canvasCandidates[i], contextStr, totalNodes, selectedNodes, CurrentArticleAction, targetContent);
                    if (prob > maxProb)
                    {
                        maxProb = prob;
                        exactMLKey = canvasCandidates[i];
                        bestType = canvasTypes[i];
                    }
                }
            }
        }

        return bestType;
    }

    private void TriggerProactiveBreathing(InterventionType bestType, string recordKey, string contextStr, bool isGlobal)
    {
        _isBreathingActive = true;
        _currentBreathingDuration = 0f;
        _idleTimer = 0f;
        _lastPredictedRecordKey = recordKey; // 极其重要：记录当前是哪个分类触发了闪烁

        float currentOffset = GetToleranceOffset(recordKey);
        Debug.Log($"<color=cyan>[Tracker 触发]</color> 区域: {contextStr} | 预测牌型: {recordKey} | 该牌专属容忍度: +{currentOffset}s");

        if (contextStr == "Article")
        {
            if (ProactiveInterventionSystem.Instance != null)
                ProactiveInterventionSystem.Instance.TriggerInterventionByType(bestType, recordKey);
        }
        else // 画布区
        {
            if (!isGlobal)
            {
                // 局部跟随
                if (proactiveButtons != null)
                {
                    foreach (var btn in proactiveButtons)
                    {
                        if (btn.interventionType == bestType && btn.gameObject.activeInHierarchy)
                        {
                            btn.StartAIBreathing(20f);
                            break;
                        }
                    }
                }
            }
            else
            {
                // 全局静默 -> 呼叫右下角
                if (GlobalProactiveButton.Instance != null)
                {
                    GlobalProactiveButton.Instance.StartGlobalBreathing(bestType);
                }
            }
        }
    }

    // =========================================================
    // 【终极闭环：5 级反馈矩阵 + 专属权重存储】
    // =========================================================

    public void OnInterventionRejected(string forcedKey = "")
    {
        _isBreathingActive = false;
        _idleTimer = 0f;
        _isAwaitingUserAction = true;

        string key = string.IsNullOrEmpty(forcedKey) ? _lastPredictedRecordKey : forcedKey;
        key = NormalizeMLKey(key); // 规范化

        if (string.IsNullOrEmpty(key)) return; // 拦截幽灵数据

        UpdateProfileRecord(key, -1.0f, penaltyExplicitReject); 
        Debug.Log($"<color=red>[ML]</color> {key} 遭显性拒绝 (-1.0分)。");
        RecordSingleMLData(key, -1.0f, "Explicit_Reject");
    }

    public void OnInterventionIgnored(string forcedKey = "")
    {
        _isBreathingActive = false;
        _idleTimer = 0f;
        _isAwaitingUserAction = true;
        string key = string.IsNullOrEmpty(forcedKey) ? _lastPredictedRecordKey : forcedKey;
        key = NormalizeMLKey(key);

        if (string.IsNullOrEmpty(key)) return;

        UpdateProfileRecord(key, -0.2f, penaltyIgnored);
        Debug.Log($"<color=yellow>[ML]</color> {key} 被搁置 (-0.2分)。");
        RecordSingleMLData(key, -0.2f, "Ignored_Timeout_Or_Overwritten");
    }

    public void OnImplicitScaffoldAccepted(string forcedKey = "")
    {
        _isBreathingActive = false;
        _idleTimer = 0f;
        string key = string.IsNullOrEmpty(forcedKey) ? _lastPredictedRecordKey : forcedKey;
        key = NormalizeMLKey(key);

        if (string.IsNullOrEmpty(key)) return;

        UpdateProfileRecord(key, 0.5f, rewardImplicit);
        Debug.Log($"<color=magenta>[ML]</color> {key} 获隐性采纳 (+0.5分)！");
        RecordSingleMLData(key, 0.5f, "Implicit_Scaffold_Accepted");
        _lastPredictedRecordKey = "";
    }

    public void OnCoCreationAccepted(string forcedKey = "")
    {
        _isBreathingActive = false;
        _idleTimer = 0f;
        string key = string.IsNullOrEmpty(forcedKey) ? _lastPredictedRecordKey : forcedKey;
        key = NormalizeMLKey(key);

        if (string.IsNullOrEmpty(key)) return;

        UpdateProfileRecord(key, 1.5f, rewardCoCreation);
        Debug.Log($"<color=cyan>[ML]</color> {key} 达成深度共创 (+1.5分)！");
        RecordSingleMLData(key, 1.5f, "Co_Creation");
        _lastPredictedRecordKey = "";
    }

    public void OnButtonClicked(string clickedTypeString)
    {
        _isBreathingActive = false;
        _idleTimer = 0f;

        // 【核心修复 1】：终于开始采用传进来的 clickedTypeString 了！
        string key = string.IsNullOrEmpty(clickedTypeString) ? _lastPredictedRecordKey : clickedTypeString;
        key = NormalizeMLKey(key);

        if (string.IsNullOrEmpty(key))
        {
            Debug.LogWarning("<color=red>[ML Tracker]</color> 警告：试图记录一个空的显性采纳！已拦截。");
            return;
        }

        UpdateProfileRecord(key, 1.0f, rewardExplicit);
        Debug.Log($"<color=green>[ML]</color> {key} 获显性采纳 (+1.0分)！");
        RecordSingleMLData(key, 1.0f, "Explicit_Adopt");
        _lastPredictedRecordKey = "";
    }

    /// <summary>
    /// 细分等级：认知抢答/提前输入 (微小正向 +0.2，减少容忍度)
    /// 用户在气泡弹出来之前就已经开始打字了，说明其思维敏捷或阅读速度快。
    /// </summary>
    public void OnPreemptiveTyping(string forcedKey = "")
    {
        string key = string.IsNullOrEmpty(forcedKey) ? _lastPredictedRecordKey : forcedKey;
        key = NormalizeMLKey(key);

        if (string.IsNullOrEmpty(key)) return;

        // 核心：步长设定为 -1.0f。每次抢答，下一次的专属等待时间就会缩短 1 秒！
        UpdateProfileRecord(key, 0.2f, rewardPreemptive);

        Debug.Log($"<color=cyan>[ML]</color> {key} 被用户提前抢答 (+0.2分)！降低容忍度使其下次更快出现 (-1.0s)。");
        RecordSingleMLData(key, 0.2f, "Preemptive_Typing");
    }

    // ==========================================
    // 用户印记持久化管理 (User Profile)
    // ==========================================
    private void LoadUserProfile()
    {
        if (File.Exists(UserProfilePath))
        {
            try
            {
                string json = File.ReadAllText(UserProfilePath);
                _userProfile = JsonUtility.FromJson<UserProfileData>(json);
                Debug.Log($"<color=green>[User Profile]</color> 成功读取被试者认知偏好档案！");
            }
            catch { _userProfile = new UserProfileData(); }
        }
        else
        {
            _userProfile = new UserProfileData();
            if (ExperimentManager.Instance != null) _userProfile.subjectID = ExperimentManager.Instance.currentSubjectID;
        }
    }

    private void SaveUserProfile()
    {
        if (_userProfile == null) return;
        try
        {
            // 如果目录不存在，自动创建
            if (!Directory.Exists(DataFolderPath)) Directory.CreateDirectory(DataFolderPath);
            string json = JsonUtility.ToJson(_userProfile, true);
            File.WriteAllText(UserProfilePath, json);
        }
        catch (Exception e) { Debug.LogError($"[User Profile] 保存档案失败: {e.Message}"); }
    }

    public float GetToleranceOffset(string categoryName)
    {
        if (_userProfile == null) return 0f;
        var record = _userProfile.actionRecords.Find(r => r.categoryName == categoryName);
        return record != null ? record.toleranceOffset : 0f;
    }

    private void UpdateProfileRecord(string categoryName, float scoreChange, float offsetDelta)
    {
        if (string.IsNullOrEmpty(categoryName) || _userProfile == null) return;

        var record = _userProfile.actionRecords.Find(r => r.categoryName == categoryName);
        if (record == null)
        {
            record = new InterventionRecord { categoryName = categoryName, toleranceOffset = 0f, totalScore = 0f, triggerCount = 0 };
            _userProfile.actionRecords.Add(record);
        }

        record.totalScore += scoreChange;
        record.triggerCount++;
        // 动态边界：最低不低于0秒，最高不超过 maxToleranceOffset
        record.toleranceOffset = Mathf.Clamp(record.toleranceOffset + offsetDelta, 0f, maxToleranceOffset);

        SaveUserProfile();
    }

    // =========================================================
    // 观察期考核与记录
    // =========================================================
    private void HandleIncomingLog(TelemetryLog log)
    {
        string eType = log.EventType;
        _idleTimer = 0f;

        if (eType.StartsWith("Canvas_") || eType.StartsWith("Article_") || eType.StartsWith("Edit_") || eType.StartsWith("Node_") || eType.StartsWith("Object_"))
        {
            if (_isBreathingActive)
            {
                Debug.Log("<color=yellow>[Tracker]</color> 侦测到用户执行了其他操作，强行打断 AI 闪烁。");
                AbortLocalBreathing();
            }

            if (_isAwaitingUserAction)
            {
                Debug.Log("<color=green>[Tracker 解锁]</color> 侦测到用户新动作，解除防挂机锁，重启 AI 观察期！");
                _isAwaitingUserAction = false;
            }
        }

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
            if (log.TargetID == "Article")
            {
                return;
            }
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
                        Debug.Log($"<color=red>[ML Tracker]</color> AI节点存活仅 {_observationTimer:F1}s，拒绝。");
                        ConcludeObservation(-1.0f, "Deleted_Instantly_Rejected");
                    }
                    else
                    {
                        Debug.Log($"<color=cyan>[ML Tracker]</color> AI节点存活 {_observationTimer:F1}s，灵感吸收。");
                        ConcludeObservation(0.5f, "Absorbed_And_Deleted");
                    }
                    return;
                }
                else if (eType == "Canvas_LinkNodes" && log.ContextInfo.Contains(_trackedTargetID))
                {
                    ConcludeObservation(1.0f, "Linked_Node");
                    return;
                }
                else if ((eType == "Edit_Node_Body_End" || eType == "Edit_Node_Title_End") && log.TargetID == _trackedTargetID)
                {
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

        string normalizedKey = NormalizeMLKey(log.ContextInfo);

        _currentDataPoint = new MLDataPoint
        {
            InterventionType = normalizedKey,
            ContextArea = "Canvas",
            CanvasNodeCount = totalNodes,
            SelectedNodeCount = selectedNodes,
            LastArticleAction = CurrentArticleAction,
            ToleranceOffset = GetToleranceOffset(normalizedKey), // 提取此时这块牌的偏移量
            Timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss")
        };
    }

    private void ConcludeObservation(float rewardScore, string reason)
    {
        _isObserving = false;
        _currentDataPoint.RewardScore = rewardScore;
        _currentDataPoint.EndReason = reason;

        // 根据考核结果，反向追加到用户档案库中，形成终极闭环！
        if (rewardScore == -1.0f) OnInterventionRejected();
        else if (rewardScore == -0.2f) OnInterventionIgnored();
        else if (rewardScore == 0.5f) OnImplicitScaffoldAccepted();
        else if (rewardScore == 1.0f) OnButtonClicked(_currentDataPoint.InterventionType); // 保持一致
        else if (rewardScore == 1.5f) OnCoCreationAccepted();

        SaveDataPointToFile(_currentDataPoint);
    }

    private void RecordSingleMLData(string categoryKey, float rewardScore, string reason)
    {
        int totalNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetAllNodes().Count : 0;
        int selectedNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetSelectedNodes().Count : 0;

        MLDataPoint pt = new MLDataPoint
        {
            InterventionType = categoryKey,
            ContextArea = (ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy) ? "Article" : "Canvas",
            CanvasNodeCount = totalNodes,
            SelectedNodeCount = selectedNodes,
            LastArticleAction = CurrentArticleAction,
            RewardScore = rewardScore,
            EndReason = reason,
            ToleranceOffset = GetToleranceOffset(categoryKey),
            Timestamp = DateTime.Now.ToString("MM-dd HH:mm:ss")
        };
        SaveDataPointToFile(pt);
    }

    private void SaveDataPointToFile(MLDataPoint data)
    {
        try
        {
            if (!Directory.Exists(DataFolderPath)) Directory.CreateDirectory(DataFolderPath);
            string json = JsonUtility.ToJson(data) + ",\n";
            File.AppendAllText(MLDataPath, json);
        }
        catch (Exception e) { Debug.LogError("[ML Tracker] 保存训练数据失败：" + e.Message); }
    }

    public void AbortLocalBreathing()
    {
        if (!_isBreathingActive) return;

        if (proactiveButtons != null)
        {
            foreach (var btn in proactiveButtons)
            {
                if (btn.isBreathing) btn.StopBreathingEarly();
            }
        }

        if (GlobalProactiveButton.Instance != null && GlobalProactiveButton.Instance.isBreathing)
        {
            GlobalProactiveButton.Instance.StopBreathingEarly();
        }

        if (CopilotActionController.Instance != null && CopilotActionController.Instance.IsAnyButtonGlowing)
        {
            CopilotActionController.Instance.StopGlowEffect();
        }

        _isBreathingActive = false;
        _idleTimer = 0f;

        _isAwaitingUserAction = true;

        if (_currentBreathingDuration < 3.0f)
        {
            Debug.Log($"<color=white>[Tracker]</color> AI闪烁仅 {_currentBreathingDuration:F1}s 就被打断，属于【动作撞车】。静默取消，不扣分。");
            _lastPredictedRecordKey = "";
        }
        else
        {
            Debug.Log($"<color=yellow>[Tracker]</color> AI闪烁 {_currentBreathingDuration:F1}s 后被用户主动操作打断，判定为无视。");
            OnInterventionIgnored(_lastPredictedRecordKey);
        }
    }

    public void SetLastPredictedRecordKey(string explicitKey)
    {
        _lastPredictedRecordKey = NormalizeMLKey(explicitKey);
    }

    private string NormalizeMLKey(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey)) return "";
        string lower = rawKey.ToLower();

        // 聊天区
        if (lower.Contains("chat") || lower.Contains("chip")) return "chat_socratic_chip";

        // AUBIM 4.0 成文区 5 大工具映射
        if (lower.Contains("coldstart") || lower.Contains("globaldraft")) return "article_coldstart";
        if (lower.Contains("expand") || lower.Contains("contextexpand")) return "article_expand";
        if (lower.Contains("stitch") || lower.Contains("contexttransition")) return "article_stitch";
        if (lower.Contains("refine") || lower.Contains("localrefine")) return "article_refine";
        if (lower.Contains("reflect") || lower.Contains("globalreview")) return "article_reflect";

        // 画布区
        if (lower.Contains("socratic")) return "proactive_socratic";
        if (lower.Contains("counter")) return "proactive_counter";
        if (lower.Contains("elaborate")) return "proactive_elaborate";
        if (lower.Contains("global")) return "proactive_global";

        return lower;
    }
}