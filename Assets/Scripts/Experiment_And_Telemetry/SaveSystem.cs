using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Threading.Tasks; // 引入 Task 用于异步保存

public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance;

    [Header("配置")]
    public string fileExtension = ".aubim";
    public string currentSaveName = "未命名项目";

    [Header("引用")]
    public ArticleGenerator articleGen; // 用于保存文章状态
    public AIChatManager chatManager;

    [Header("自动保存配置")]
    public bool isAutoSaveEnabled = true;   // 开关
    public float autoSaveInterval = 300f;

    // =========================================================
    // 【核心改造】：统一管理存档文件夹路径，接入实验管家
    // =========================================================
    private string SaveFolderPath
    {
        get
        {
            // 优先：如果接入了实验管家，直接在当前受试者文件夹下建一个 Projects 目录
            if (ExperimentManager.Instance != null)
            {
                string userRoot = ExperimentManager.GetUserFolderPath();
                string projectsFolder = Path.Combine(userRoot, "Projects");
                if (!Directory.Exists(projectsFolder)) Directory.CreateDirectory(projectsFolder);
                return projectsFolder;
            }

            // 兜底：如果没有挂载实验管家，默认存在桌面的 AUBIM_项目存档
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string fallbackPath = Path.Combine(desktop, "AUBIM_项目存档");
            if (!Directory.Exists(fallbackPath)) Directory.CreateDirectory(fallbackPath);
            return fallbackPath;
        }
    }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 确保文件夹存在
        if (!Directory.Exists(SaveFolderPath))
        {
            Directory.CreateDirectory(SaveFolderPath);
        }

        if (isAutoSaveEnabled)
        {
            StartCoroutine(AutoSaveLoop());
        }
    }

    // ========================================================================
    // 1. 保存功能 (Save) - 优化为异步写入，防止自动保存时卡顿
    // ========================================================================
    public async void SaveProject(string saveName)
    {
        if (saveName != "AutoSave") currentSaveName = saveName;

        ProjectSaveData data = new ProjectSaveData();
        data.SaveName = saveName;
        data.Timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 确保列表已初始化（防止空引用报错）
        if (data.Nodes == null) data.Nodes = new List<NodeSaveDTO>();

        // A. 保存文章草稿 (4.0 已废除独立的 aiSuggestionInput，AI 记录全部在 ChatHistory 中)
        if (articleGen != null)
        {
            data.ArticleDraft = articleGen.mainBodyInput != null ? articleGen.mainBodyInput.text : "";
            data.AISuggestion = ""; // 保持结构体兼容，但不再保存旧 UI 内容
        }

        // B. 保存聊天记录
        if (chatManager != null)
        {
            data.ChatHistory = chatManager.GetHistoryData();
        }

        List<BaseNodeController> nodesToSave = new List<BaseNodeController>();

        // 1. 抓取中心画布区的所有节点
        if (NodeCardManager.Instance.cardContainer != null)
        {
            var canvasNodes = NodeCardManager.Instance.cardContainer.GetComponentsInChildren<BaseNodeController>(true);
            nodesToSave.AddRange(canvasNodes);
        }

        // C. 遍历打包
        foreach (var node in nodesToSave)
        {
            if (node == null) continue;

            // 过滤回收站
            if (NodeCardManager.Instance.recycleBin != null && node.transform.IsChildOf(NodeCardManager.Instance.recycleBin))
                continue;

            NodeSaveDTO dto = new NodeSaveDTO();
            dto.ID = node.NodeID;
            dto.Type = "NodeCard";

            // 数据保护
            if (node.Data != null)
            {
                dto.Title = node.Data.Title;
                dto.Content = node.Data.Content;
            }
            else
            {
                dto.Title = "";
                dto.Content = "";
            }

            RectTransform rt = node.GetComponent<RectTransform>();
            dto.AnchoredPosition = rt.anchoredPosition;
            dto.Width = rt.rect.width;
            dto.Height = rt.rect.height;
            dto.SiblingIndex = node.transform.GetSiblingIndex();

            dto.ContainerType = "Canvas";
            dto.ParentID = node.parentNode != null ? node.parentNode.NodeID : "null";

            data.Nodes.Add(dto);
        }

        // D. 序列化为 JSON
        string json = JsonUtility.ToJson(data, true);

        // E. 核心修复：更新行为日志的上下文前缀，保持数据连贯性
        if (UserBehaviorSystem.Instance != null && saveName != "AutoSave")
        {
            UserBehaviorSystem.Instance.RenameLogContext(saveName);
        }

        if (!Directory.Exists(SaveFolderPath)) Directory.CreateDirectory(SaveFolderPath);
        string path = Path.Combine(SaveFolderPath, saveName + fileExtension);

        try
        {
            // 【性能优化】：使用异步写入，项目数据再大也不会卡死 Unity 主线程
            await File.WriteAllTextAsync(path, json);
            Debug.Log($"<color=green>[项目系统]</color> 存档成功，已存入当前用户目录: {path} (节点数: {data.Nodes.Count})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[项目系统] 异步存档写入失败: {e.Message}");
        }

        // 【删减说明】：删除了原有的 UserBehaviorSystem.Instance.SaveLogsToDisk();
        // 因为行为日志现已改为基于 JSONL 的实时增量打点，不需要随项目一并做全量刷盘。
    }

    // ========================================================================
    // 2. 读取功能 (Load)
    // ========================================================================
    public void LoadProject(string saveName)
    {
        string path = Path.Combine(SaveFolderPath, saveName + fileExtension);

        if (!File.Exists(path))
        {
            Debug.LogError("[项目系统] 存档不存在: " + path);
            return;
        }

        try
        {
            currentSaveName = saveName;

            string json = File.ReadAllText(path);
            ProjectSaveData data = JsonUtility.FromJson<ProjectSaveData>(json);
            StartCoroutine(ReconstructScene(data));

            if (UserBehaviorSystem.Instance != null)
            {
                UserBehaviorSystem.Instance.SwitchLogContext(saveName);
            }

            Debug.Log($"<color=green>[项目系统]</color> 成功读取当前用户的存档: {saveName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[项目系统] 读取存档失败: {e.Message}");
        }
    }

    // ========================================================================
    // 3. 重建场景 (Reconstruct)
    // ========================================================================
    private IEnumerator ReconstructScene(ProjectSaveData data)
    {
        yield return StartCoroutine(ClearSceneRoutine());

        var dragRouter = FindObjectOfType<CanvasPanZoomController>();
        if (dragRouter != null) dragRouter.enabled = false;

        if (chatManager != null && data.ChatHistory != null)
        {
            chatManager.LoadHistoryData(data.ChatHistory);
        }

        Dictionary<string, BaseNodeController> canvasMap = new Dictionary<string, BaseNodeController>();

        // 第一步：实例化所有节点，恢复基础数据
        foreach (var dto in data.Nodes)
        {
            GameObject prefab = NodeCardManager.Instance.nodeCardPrefab;
            Transform parentContainer = NodeCardManager.Instance.cardContainer;

            if (prefab != null)
            {
                GameObject obj = Instantiate(prefab, parentContainer);
                BaseNodeController ctrl = obj.GetComponent<BaseNodeController>();

                ctrl.Data = new NodeData(CardType.NodeCard);
                ctrl.Data.ID = dto.ID;
                // 【核心修复】：读取存档时清洗换行符，防止旧存档污染
                ctrl.Data.Title = string.IsNullOrEmpty(dto.Title) ? "" : dto.Title.Replace("\r", "");
                ctrl.Data.Content = string.IsNullOrEmpty(dto.Content) ? "" : dto.Content.Replace("\r", "");

                RectTransform rt = obj.GetComponent<RectTransform>();
                rt.anchoredPosition = dto.AnchoredPosition;

                if (ctrl is NodeController nodeCtrl)
                {
                    if (dto.Width > 10)
                    {
                        nodeCtrl.RestoreSize(dto.Width);
                    }
                }

                if (!canvasMap.ContainsKey(dto.ID)) canvasMap.Add(dto.ID, ctrl);
                NodeCardManager.Instance.RegisterNodeCard(dto.ID, ctrl);

                ctrl.RefreshUI();
            }
        }

        // 第二步：根据 ParentID 重建父子连线拓扑
        foreach (var dto in data.Nodes)
        {
            if (canvasMap.ContainsKey(dto.ID))
            {
                var me = canvasMap[dto.ID];
                if (dto.ParentID != "null" && canvasMap.ContainsKey(dto.ParentID))
                {
                    var parentNode = canvasMap[dto.ParentID];
                    NodeLinkManager.Instance.CreateConnection(parentNode, me);
                }
            }
        }

        // 第三步：恢复层级排位 (SiblingIndex)
        foreach (var dto in data.Nodes)
        {
            if (canvasMap.ContainsKey(dto.ID))
            {
                canvasMap[dto.ID].transform.SetSiblingIndex(dto.SiblingIndex);
            }
        }

        if (articleGen != null)
        {
            string cleanDraft = string.IsNullOrEmpty(data.ArticleDraft) ? "" : data.ArticleDraft.Replace("\r", "");
            articleGen.RestoreArticleData(cleanDraft); // 4.0 仅需恢复正文草稿
        }

        yield return null;
        Canvas.ForceUpdateCanvases();

        // 第四步：触发自动布局
        if (AutoLayoutSystem.Instance != null)
        {
            foreach (var node in canvasMap.Values)
            {
                if (node.parentNode == null) AutoLayoutSystem.Instance.RefreshLayout(node);
            }
        }

        if (dragRouter != null) dragRouter.enabled = true;
    }

    // ========================================================================
    // 4. 获取文件列表 (用于 UI 显示)
    // ========================================================================
    public List<string> GetSaveFiles()
    {
        string path = SaveFolderPath;

        if (!Directory.Exists(path)) Directory.CreateDirectory(path);

        var info = new DirectoryInfo(path);
        var files = info.GetFiles("*" + fileExtension)
                        .OrderByDescending(p => p.LastWriteTime)
                        .Select(p => Path.GetFileNameWithoutExtension(p.Name))
                        .ToList();
        return files;
    }

    private IEnumerator AutoSaveLoop()
    {
        while (isAutoSaveEnabled)
        {
            yield return new WaitForSeconds(autoSaveInterval);
            SaveProject("AutoSave");
            Debug.Log($"[自动保存] 系统已异步存档于: {System.DateTime.Now:HH:mm:ss}");
            if (ToastSystem.Instance != null)
                ToastSystem.Instance.Show("系统已自动保存");
        }
    }

    // =========================================================
    // 新建项目功能
    // =========================================================
    public void CreateNewProject()
    {
        currentSaveName = "未命名项目_" + DateTime.Now.ToString("MMdd_HHmm");

        StartCoroutine(ClearSceneRoutine());

        if (UserBehaviorSystem.Instance != null)
        {
            string tempName = "Unsaved_" + DateTime.Now.ToString("HHmmss");
            UserBehaviorSystem.Instance.SwitchLogContext(tempName);
        }

        Debug.Log("[System] 已新建空白项目");
    }

    // 清理逻辑
    private IEnumerator ClearSceneRoutine()
    {
        var dragRouter = FindObjectOfType<CanvasPanZoomController>();
        if (dragRouter != null) dragRouter.enabled = false;

        if (NodeCardManager.Instance != null)
        {
            NodeCardManager.Instance.CancelSelection();
            NodeCardManager.Instance.ClearRegistries();
        }

        if (NodeCardManager.Instance.cardContainer != null)
        {
            foreach (Transform child in NodeCardManager.Instance.cardContainer) Destroy(child.gameObject);
        }

        if (NodeLinkManager.Instance != null)
        {
            NodeLinkManager.Instance.ClearAll();
        }

        if (chatManager != null)
        {
            chatManager.ClearChatSession();
        }

        if (articleGen != null)
        {
            articleGen.RestoreArticleData(""); // 4.0 仅清空正文
        }

        yield return null;
        yield return null;

        if (dragRouter != null) dragRouter.enabled = true;
    }
}