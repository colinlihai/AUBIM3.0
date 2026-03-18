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

// ==========================================
// 主系统
// ==========================================
public class InterventionTracker : MonoBehaviour
{
    public static InterventionTracker Instance;

    [Header("UI 联动")]
    public List<ProactiveButtonUI> proactiveButtons;

    [Header("观察期设置 (AUBIM 自适应双轨制)")]
    public float baseCanvasStall = 20f;
    public float baseArticleStall = 15f;
    public float observationWindow = 45f;
    public float maxToleranceOffset = 30f;

    public string CurrentArticleAction { get; private set; } = "None";

    [Header("状态锁")]
    public bool isSuspendedByChat = false;
    public bool isAIProcessing = false;
    private float _readingBuffer = 0f;
    private bool _isAwaitingUserAction = false;

    private float _idleTimer = 0f;
    private bool _isBreathingActive = false;
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
        _readingBuffer = Mathf.Clamp(charCount * 0.1f, 0f, 120f);
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

    // ==========================================
    // 核心循环 Update
    // ==========================================
    void Update()
    {
        if (_isBreathingActive)
        {
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

            if (!isPhysicallyBreathing)
            {
                Debug.Log("<color=yellow>[Tracker 守卫]</color> 物理 UI 已消失，清理静默残留并结算为搁置！");
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

            if (_isAwaitingUserAction)
            {
                _idleTimer = 0f;
                return;
            }

            bool isUsingIME = !string.IsNullOrEmpty(Input.compositionString);
            bool hasTextInput = !string.IsNullOrEmpty(Input.inputString);

            if (Input.anyKey || isUsingIME || hasTextInput)
            {
                _idleTimer = 0f;
                _readingBuffer = 0f;
            }
            else if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2) || Mathf.Abs(Input.mouseScrollDelta.y) > 0.1f)
            {
                _idleTimer = 0f;
                if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
                {
                    _readingBuffer = 0f;
                }
            }
            else
            {
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
    // 预测与触发逻辑 (完美对齐分类器 8 大特征)
    // ==========================================
    private InterventionType GetBestPredictedType(out string contextStr, out bool isGlobal, out string exactMLKey)
    {
        bool isArticleActive = ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy;
        int totalNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetAllNodes().Count : 0;
        int selectedNodes = NodeCardManager.Instance != null ? NodeCardManager.Instance.GetSelectedNodes().Count : 0;

        contextStr = isArticleActive ? "Article" : "Canvas";
        isGlobal = (!isArticleActive && selectedNodes == 0);
        exactMLKey = "";
        InterventionType bestType = InterventionType.None;

        if (InterventionClassifier.Instance == null) return bestType;

        // 1. 提取当前焦点文本，传给分类器进行“启发式偏置 (问号/字数识别)”
        string targetContent = selectedNodes > 0 && NodeCardManager.Instance.GetSelectedNodes()[0].Data != null
                               ? NodeCardManager.Instance.GetSelectedNodes()[0].Data.Content : "";

        float maxProb = -1f;

        if (isArticleActive)
        {
            // =========================================================
            // 成文区：根据物理光标位置，精确定位 4 个 Stage 之一
            // =========================================================
            string text = ArticleGenerator.Instance.mainBodyInput.text;
            bool isFocused = ArticleGenerator.Instance.mainBodyInput.isFocused;
            int cursorIndex = ArticleGenerator.Instance.mainBodyInput.selectionFocusPosition;

            if (string.IsNullOrWhiteSpace(text))
            {
                exactMLKey = "article_coldstart";
                bestType = InterventionType.ArticleGap;
            }
            else if (!isFocused)
            {
                exactMLKey = "article_reflect";
                bestType = InterventionType.ArticleReflect;
            }
            else if (text.Length - cursorIndex <= 15)
            {
                exactMLKey = "article_expand";
                bestType = InterventionType.ArticleGap;
            }
            else
            {
                exactMLKey = "article_stitch";
                bestType = InterventionType.ArticleGap;
            }

            // 直接预测这个精准的 Key，并将文本传进去
            maxProb = InterventionClassifier.Instance.PredictAcceptanceProbability(exactMLKey, contextStr, totalNodes, selectedNodes, CurrentArticleAction, targetContent);
        }
        else
        {
            // =========================================================
            // 画布区：在 3 个候选项中让大脑算一下，谁的概率最高
            // =========================================================
            if (isGlobal)
            {
                exactMLKey = "proactive_global";
                bestType = InterventionType.Socratic; // 全局借用反思的UI结构来触发
            }
            else
            {
                string[] canvasCandidates = new string[] { "proactive_socratic", "proactive_counter", "proactive_elaborate" };
                InterventionType[] canvasTypes = new InterventionType[] { InterventionType.Socratic, InterventionType.Counter, InterventionType.Elaborate };

                for (int i = 0; i < canvasCandidates.Length; i++)
                {
                    // 传入 targetContent，让模型知道被试者是不是正在打问号！
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
        _idleTimer = 0f;
        _lastPredictedRecordKey = recordKey; // 极其重要：记录当前是哪个分类触发了闪烁

        float currentOffset = GetToleranceOffset(recordKey);
        Debug.Log($"<color=cyan>[Tracker 触发]</color> 区域: {contextStr} | 预测牌型: {recordKey} | 该牌专属容忍度: +{currentOffset}s");

        if (contextStr == "Article")
        {
            _isBreathingActive = false; // 成文区直接空投 UI 提示
            if (ProactiveInterventionSystem.Instance != null)
                ProactiveInterventionSystem.Instance.TriggerInterventionByType(bestType);
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
        string key = string.IsNullOrEmpty(forcedKey) ? _lastPredictedRecordKey : forcedKey;
        key = NormalizeMLKey(key); // 规范化

        if (string.IsNullOrEmpty(key)) return; // 拦截幽灵数据

        UpdateProfileRecord(key, -1.0f, +10f); // 假设我们上一版改成了 10f
        Debug.Log($"<color=red>[ML]</color> {key} 遭显性拒绝 (-1.0分)。");
        RecordSingleMLData(key, -1.0f, "Explicit_Reject");
    }

    public void OnInterventionIgnored(string forcedKey = "")
    {
        _isBreathingActive = false;
        _idleTimer = 0f;
        string key = string.IsNullOrEmpty(forcedKey) ? _lastPredictedRecordKey : forcedKey;
        key = NormalizeMLKey(key);

        if (string.IsNullOrEmpty(key)) return;

        UpdateProfileRecord(key, -0.2f, +3f);
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

        UpdateProfileRecord(key, 0.5f, -1.5f);
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

        UpdateProfileRecord(key, 1.5f, -5f);
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

        UpdateProfileRecord(key, 1.0f, -3f);
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
        UpdateProfileRecord(key, 0.2f, -1.0f);

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

        _isBreathingActive = false;
        _idleTimer = 0f;

        _isAwaitingUserAction = true;

        OnInterventionIgnored(_lastPredictedRecordKey);
    }

    // =========================================================
    // 【核心修复 2：数据规范化漏斗】解决键值分裂与大小写不一致
    // =========================================================
    private string NormalizeMLKey(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey)) return "";
        string lower = rawKey.ToLower();

        // 聊天区
        if (lower.Contains("chat") || lower.Contains("chip")) return "chat_socratic_chip";

        // 成文区 (4 Stage)
        if (lower.Contains("coldstart") || lower.Contains("stage0")) return "article_coldstart";
        if (lower.Contains("expand")) return "article_expand";
        if (lower.Contains("stitch")) return "article_stitch";
        if (lower.Contains("reflect")) return "article_reflect";

        // 画布区 (4 Proactive)
        if (lower.Contains("socratic")) return "proactive_socratic";
        if (lower.Contains("counter")) return "proactive_counter";
        if (lower.Contains("elaborate")) return "proactive_elaborate";
        if (lower.Contains("global")) return "proactive_global";

        // 兜底防线：如果UI传了类似 "ManualTriggered"
        if (lower.Contains("manual")) return "proactive_global";

        // 极致兜底
        return lower;
    }
}