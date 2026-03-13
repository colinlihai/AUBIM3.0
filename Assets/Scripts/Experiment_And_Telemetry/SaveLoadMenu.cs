using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.IO;
using System;

public class SaveLoadMenu : MonoBehaviour
{
    [Header("UI 组件 - Header")]
    public TMP_InputField projectNameInput;
    public Button saveBtn;
    public Button toggleBtn;
    public Button newProjectBtn;

    [Header("UI 组件 - List")]
    public Transform listContent;        // Content 父节点
    public GameObject fileItemPrefab;    // 列表项预制体

    [Header("控制引用")]
    public TopPanelDrawer drawerController; // [关键] 引用上面的脚本

    private string _defaultName = "MyProject";

    private string CurrentProjectsFolder
    {
        get
        {
            if (ExperimentManager.Instance != null)
            {
                return Path.Combine(ExperimentManager.GetUserFolderPath(), "Projects");
            }
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            return Path.Combine(desktop, "AUBIM_项目存档");
        }
    }

    void Start()
    {
        if (saveBtn) saveBtn.onClick.AddListener(OnSaveClicked);

        if (toggleBtn && drawerController)
        {
            // 【核心修复 1】：每次点击菜单按钮时，先强制刷新一次列表，再拉出抽屉！绝对防呆。
            toggleBtn.onClick.AddListener(() =>
            {
                RefreshFileList();
                drawerController.ToggleDrawer();
            });
        }

        if (newProjectBtn != null)
        {
            newProjectBtn.onClick.AddListener(OnNewProjectClicked);
        }
        if (projectNameInput) projectNameInput.text = _defaultName;

        // 【核心修复 2】：订阅全局管家的发令枪！
        ExperimentManager.OnExperimentStarted += OnUserLoggedIn;

        // 软件刚开时（未登录前）刷一次兜底
        RefreshFileList();
    }

    // 防内存泄漏
    void OnDestroy()
    {
        ExperimentManager.OnExperimentStarted -= OnUserLoggedIn;
    }

    // 当用户在登录界面点击“开始”后触发
    private void OnUserLoggedIn(string subjectID)
    {
        Debug.Log($"[SaveLoadMenu] 检测到用户 {subjectID} 登录，正在刷新专属项目列表...");
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        // 1. 清理
        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            Transform child = listContent.GetChild(i);
            child.SetParent(null); // 立刻脱离父子关系
            Destroy(child.gameObject);
        }

        // 2. 生成列表
        List<string> files = SaveSystem.Instance.GetSaveFiles();
        string ext = SaveSystem.Instance != null ? SaveSystem.Instance.fileExtension : ".aubim";

        foreach (var file in files)
        {
            GameObject obj = Instantiate(fileItemPrefab, listContent);
            FileItemController item = obj.GetComponent<FileItemController>();

            string fullPath = Path.Combine(CurrentProjectsFolder, file + ext);
            string timeStr = File.Exists(fullPath) ? File.GetLastWriteTime(fullPath).ToString("MM/dd HH:mm") : "";

            item.Init(file, timeStr, this);
        }

        // 3. 通知 Drawer 重新计算高度
        if (drawerController != null)
        {
            drawerController.RefreshHeight();
        }
    }

    // --- 业务逻辑 ---

    public void OnLoadRequested(string fileName)
    {
        SaveSystem.Instance.LoadProject(fileName);
        if (drawerController) drawerController.CloseDrawer();
        if (projectNameInput) projectNameInput.text = fileName;
    }

    public void OnDeleteFile(string fileName)
    {
        string folderPath = CurrentProjectsFolder;
        string ext = SaveSystem.Instance != null ? SaveSystem.Instance.fileExtension : ".aubim";
        string fullPath = Path.Combine(folderPath, fileName + ext);

        if (File.Exists(fullPath))
        {
            try
            {
                File.Delete(fullPath);
                Debug.Log($"<color=yellow>[UI系统]</color> 文件已物理删除: {fullPath}");
                RefreshFileList();
            }
            catch (Exception e)
            {
                Debug.LogError($"删除出错: {e.Message}");
            }
        }
        else
        {
            Debug.LogError($"[Delete Failed] 文件未找到，路径可能有误:\n目标路径: {fullPath}");
        }
    }

    private void OnNewProjectClicked()
    {
        SaveSystem.Instance.CreateNewProject();

        if (projectNameInput)
        {
            projectNameInput.text = "NewProject_" + DateTime.Now.ToString("MMdd");
        }

        if (drawerController) drawerController.CloseDrawer();
    }

    private void OnSaveClicked()
    {
        string name = projectNameInput.text;
        if (string.IsNullOrWhiteSpace(name)) name = _defaultName;

        SaveSystem.Instance.SaveProject(name);
        RefreshFileList();
    }

    public void OnItemSelected(FileItemController item, string fileName)
    {
        if (projectNameInput) projectNameInput.text = fileName;
    }
}