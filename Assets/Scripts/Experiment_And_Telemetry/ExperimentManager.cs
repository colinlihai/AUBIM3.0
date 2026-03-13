using UnityEngine;
using System.IO;
using System;

public class ExperimentManager : MonoBehaviour
{
    public static ExperimentManager Instance;

    // 【新增】：全局发令枪事件。当用户在 UI 上点击“开始”时，触发此事件
    public static event Action<string> OnExperimentStarted;

    [Header("当前受试者 (运行时由 UI 注入)")]
    public string currentSubjectID = "";
    public bool isInitialized = false; // 系统是否已正式启动

    void Awake()
    {
        if (Instance == null) { Instance = this; }
        else if (Instance != this) { Destroy(gameObject); }
    }

    // 【新增】：供前端 UI 登录按钮调用的方法
    public void StartExperimentWithID(string subjectID)
    {
        if (string.IsNullOrWhiteSpace(subjectID)) return;

        currentSubjectID = subjectID;
        isInitialized = true;

        // 确保文件夹存在
        string folderPath = GetUserFolderPath();
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Debug.Log($"[实验管家] 为新用户创建了专属数据舱: {folderPath}");
        }
        else
        {
            Debug.Log($"[实验管家] 识别到老用户，准备加载历史数据: {folderPath}");
        }

        // 鸣枪！通知大脑和听诊器：“路径准备好了，你们可以开始干活了！”
        OnExperimentStarted?.Invoke(currentSubjectID);
    }

    public static string GetUserFolderPath()
    {
        if (Instance == null || string.IsNullOrWhiteSpace(Instance.currentSubjectID))
        {
            // 防御性编程：如果有人在没登录时就强行索要路径，给一个缓存区
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AUBIM_Data", "_TempCache");
            if (!Directory.Exists(fallback)) Directory.CreateDirectory(fallback);
            return fallback;
        }

        string baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AUBIM_Data");
        return Path.Combine(baseFolder, Instance.currentSubjectID);
    }
}