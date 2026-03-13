using UnityEngine;
using UnityEngine.UI;
using TMPro; // 必须使用 TextMeshPro
using System.IO;
using System;
using System.Collections.Generic;

public class ExperimentLoginUI : MonoBehaviour
{
    [Header("UI 引用")]
    public TMP_InputField idInputField; // 输入新受试者 ID 的文本框
    public TMP_Dropdown idDropdown;     // 选择已有 ID 的下拉菜单
    public Button startButton;          // 开始测试按钮
    public GameObject loginPanel;       // 整个登录界面遮罩面板

    private string _baseFolder;

    void Start()
    {
        // 确保一开始登录面板是激活的，挡住后面的所有操作
        if (loginPanel != null) loginPanel.SetActive(true);

        // 获取桌面数据根目录
        _baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "AUBIM_Data");

        // 自动扫描并填充下拉菜单
        PopulateDropdown();

        // 绑定按钮点击事件
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartClicked);
        }
    }

    private void PopulateDropdown()
    {
        if (idDropdown == null) return;

        idDropdown.ClearOptions();

        // 第一项作为提示语
        List<string> options = new List<string> { "选择ID" };

        // 扫描 AUBIM_Data 下所有的文件夹
        if (Directory.Exists(_baseFolder))
        {
            string[] directories = Directory.GetDirectories(_baseFolder);
            foreach (string dir in directories)
            {
                // 只提取文件夹的名字（比如 "User_01"），去掉前面的冗长路径
                options.Add(Path.GetFileName(dir));
            }
        }

        idDropdown.AddOptions(options);

        // 绑定下拉菜单的切换事件
        idDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
    }

    // 当用户在下拉菜单里选中了一个老用户时触发
    private void OnDropdownValueChanged(int index)
    {
        if (index > 0) // 跳过第一个提示项
        {
            // 极其贴心的设计：自动把选中的老名字，填入到输入框里！
            // 这样系统最终只需要读取输入框里的字就可以了
            idInputField.text = idDropdown.options[index].text;
        }
        else
        {
            // 如果选回了提示项，就清空输入框，准备输入新 ID
            idInputField.text = "";
        }
    }

    private void OnStartClicked()
    {
        // 最终的裁决：永远以 InputField 里的文字为准
        string inputID = idInputField.text.Trim();

        if (string.IsNullOrEmpty(inputID))
        {
            Debug.LogWarning("[实验系统] ID 不能为空，请重新输入或在下拉菜单中选择！");
            return; // 拒绝放行
        }

        // 1. 呼叫管家，传入 ID，打响发令枪！(唤醒背后的 AI 和记录器)
        ExperimentManager.Instance.StartExperimentWithID(inputID);

        // 2. 撤掉防弹玻璃
        if (loginPanel != null)
        {
            loginPanel.SetActive(false);
        }
    }
}