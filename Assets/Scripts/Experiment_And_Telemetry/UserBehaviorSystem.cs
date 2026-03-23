using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

// [核心枚举] 对应 AUBIM 指标 (PDA/BR/SWI/RHY) 及 ML 特征源
public enum BehaviorEventType
{
    // ==========================================
    // 0. 会话级 (Session) - 标注数据的边界
    // ==========================================
    SessionStart,
    SessionEnd,

    // ==========================================
    // 1. 发散行为 (Divergent) -> 正向特征 (PDA High)
    // ==========================================
    Canvas_CreateNode,      // 新建节点 (双击/Tab/Enter)
    Canvas_LinkNodes,       // 建立连线
    Canvas_Node_Move,       // 移动节点 (大幅度布局调整)
    Canvas_Node_DetachMove,
    Canvas_ReorderNode,

    // ==========================================
    // 3. 回溯行为 (Regressive) -> 负向特征 (BR High -> 需要介入?)
    // ==========================================
    // 这里的频次过高通常意味着 classifier 应该输出 "Stall/NeedHelp"
    Action_Undo,            // 撤销
    Action_Redo,            // 重做
    Object_Delete,          // 删除 (Node/Leaf/Group)
    Link_Break,             // 断开连接

    // ==========================================
    // 4. 产出行为 (Production) -> 节奏特征 (RHY)
    // ==========================================
    // 结合 Value 字段 (字数变化) 分析是“大段输出”还是“反复修改”
    Edit_Node_Title_End,    // 节点标题修改完成
    Edit_Node_Body_End,     // 节点正文修改完成 (Value = 字数变化量)

    // ==========================================
    // 5. 探索与交互 (Exploration) -> 注意力特征 (SWI)
    // ==========================================
    View_PanZoom,           // 漫游画布 (如果高频但无Create/Edit，可能是迷茫)
    Selection_Change,       // 切换选中目标

    Article_Open,
    Article_Close,
    Article_Export,
    Article_ImportOutline,
    Article_ClearSuggestion,
    Article_Generate_Global,  // 触发了全局生成
    Article_Generate_Node,    // 触发了针对选中节点的生成
    Article_Generate_Local,    // 触发了局部润色

    Edit_Article_Body,
    Article_Adopt_AI,
    Article_Delete_Text,

    // ==========================================
    // 6. AI 介入反馈 (AI Interaction) -> 标签特征 (Label)
    // ==========================================
    // 用于验证分类器介入后的效果
    AI_AutoTitle_Triggered, // 自动拟题触发
    AI_Chat_CreateNode,
    AI_Chat_Query,          // 用户主动向 Chat 提问
    AI_Chat_Response,   // AI 回复到达 (开始阅读)
    AI_Chat_DeleteBubble, // 用户手动删除了某条聊天记录
    AI_Chat_ExtractClick,   // 点击提取气泡节点
    AI_Intervention_Triggered,
    AI_Intervention_Socratic, // 触发苏格拉底追问 (发散/启发)
    AI_Intervention_Counter,  // 触发反向论证 (批判/收束)
    AI_Intervention_Rejected,      // 看完后删除了 AI 节点
    AI_Intervention_Extended,      // 顺着 AI 节点往下建了子节点 (最高价值)
    AI_Intervention_Elaborate,
    AI_Intervention_Internalized,  // 修改了 AI 节点的文字

    // ==========================================
    // 7. 状态标记 (State Markers) -> 训练数据的 Ground Truth (真值)
    // ==========================================
    State_Stall_Detected,   // 系统判定进入停滞
    State_Stall_Recovered   // 系统判定恢复
}

[Serializable]
public class TelemetryLog
{
    public string Timestamp;     // 时间戳 (绝对时间)
    public float TimeSinceStart; // 相对时间 (用于训练模型时对齐时间轴)
    public string EventType;     // 事件类型

    public string TargetID;      // 操作对象的 ID (NodeID / GroupID)
    public string ContextInfo;   // 上下文 (如 "WordCount:50->60", "Undo:MoveNode")

    public float Value;          // 核心数值 (用于归一化计算)
                                 // - Edit: 字数变化量 (Delta)
                                 // - Pan: 移动距离
                                 // - Stall: 停滞持续秒数

    public string ProjectName;
}

public class UserBehaviorSystem : MonoBehaviour
{
    public static UserBehaviorSystem Instance;

    [Header("实验配置")]
    public string ParticipantID = "User_001";
    public static event Action<TelemetryLog> OnEventLogged;

    private string _currentSaveName = "";
    private float _startTime;

    private string _sessionID;

    // 日志保存的根目录
    private string LogFolderPath
    {
        get
        {
            // 优先使用全局管家的路径 (实现绝对的数据隔离)
            if (ExperimentManager.Instance != null)
            {
                return ExperimentManager.GetUserFolderPath();
            }

            // 兜底方案：如果没挂载管家，就在 AUBIM_Data 下建一个 DefaultUser 文件夹
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fallbackPath = Path.Combine(desktop, "AUBIM_Data", "DefaultUser");
            if (!Directory.Exists(fallbackPath)) Directory.CreateDirectory(fallbackPath);
            return fallbackPath;
        }
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        // 订阅发令枪
        ExperimentManager.OnExperimentStarted += InitializeLogging;
    }

    private void InitializeLogging(string subjectID)
    {
        Debug.Log("[日志系统] 收到管家指令，开始打点记录...");
        if (!Directory.Exists(LogFolderPath)) Directory.CreateDirectory(LogFolderPath);

        _startTime = Time.time;
        _sessionID = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        // 系统正式开启的第一条记录
        LogEvent(BehaviorEventType.SessionStart, "System", $"UserLogin:{subjectID}");
    }

    void OnDestroy()
    {
        ExperimentManager.OnExperimentStarted -= InitializeLogging;
    }

    void OnApplicationQuit()
    {
        LogEvent(BehaviorEventType.SessionEnd, "System", "ApplicationQuit");
    }

    // =========================================================
    // 切换上下文：只打点，不换文件！
    // =========================================================
    public void SwitchLogContext(string saveName)
    {
        if (!string.IsNullOrEmpty(_currentSaveName) && _currentSaveName != "Unsaved")
        {
            LogEvent(BehaviorEventType.SessionEnd, "System", $"Leave_Project:{_currentSaveName}");
        }

        _currentSaveName = string.IsNullOrEmpty(saveName) ? "Unsaved" : saveName;
        LogEvent(BehaviorEventType.SessionStart, "System", $"Resume_Project:{_currentSaveName}");
    }

    public void RenameLogContext(string newSaveName)
    {
        if (_currentSaveName == newSaveName)
        {
            LogEvent(BehaviorEventType.SessionEnd, "System", $"Save_Snapshot:{newSaveName}");
            return;
        }

        LogEvent(BehaviorEventType.SessionEnd, "System", $"Rename_Project: From {_currentSaveName} to {newSaveName}");
        _currentSaveName = newSaveName;
    }

    // =========================================================
    // 核心优化：增量异步打点 (固定写入 Session 文件)
    // =========================================================
    public void LogEvent(BehaviorEventType type, string targetID = "System", string info = "", float value = 0)
    {
        float relativeTime = Time.time - _startTime;

        TelemetryLog newLog = new TelemetryLog
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
            TimeSinceStart = relativeTime,
            EventType = type.ToString(),
            TargetID = targetID,
            ContextInfo = info,
            Value = value,
            ProjectName = _currentSaveName // 【新增】：写入当时的项目名
        };

        OnEventLogged?.Invoke(newLog);

        string jsonLine = JsonUtility.ToJson(newLog);

        string currentUserID = ExperimentManager.Instance != null ? ExperimentManager.Instance.currentSubjectID : "DefaultUser";

        // 【核心修复】：文件名永远使用 _sessionID 锚定，绝不中途断裂！
        string filename = $"AUBIM_Log_{currentUserID}_{_sessionID}.jsonl";
        string path = Path.Combine(LogFolderPath, filename);

        AppendLogAsync(path, jsonLine + "\n");
    }

    // 异步写入方法，解决 IO 卡顿
    private async void AppendLogAsync(string path, string content)
    {
        try
        {
            // 使用 StreamWriter 追加文本
            using (StreamWriter writer = new StreamWriter(path, true)) // true 表示追加模式
            {
                await writer.WriteAsync(content);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[日志系统] 异步写入日志失败: {e.Message}");
        }
    }
}