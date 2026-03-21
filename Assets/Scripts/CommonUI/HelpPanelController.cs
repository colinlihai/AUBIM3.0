using UnityEngine;
using UnityEngine.UI;

public class HelpPanelController : MonoBehaviour
{
    [Header("触发按钮")]
    public Button toggleButton; // 拖入你放在右上角的“功能说明”按钮

    [Header("说明文档面板")]
    public GameObject helpPanel; // 拖入你做好的带背景的整体说明面板 (Panel/Text)

    void Start()
    {
        // 1. 初始化时，确保说明面板是默认隐藏的
        if (helpPanel != null)
        {
            helpPanel.SetActive(false);
        }

        // 2. 为按钮绑定点击事件
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(OnToggleClicked);
        }
    }

    private void OnToggleClicked()
    {
        if (helpPanel != null)
        {
            // 每次点击，切换面板的 开启/关闭 状态
            bool isCurrentlyActive = helpPanel.activeSelf;
            helpPanel.SetActive(!isCurrentlyActive);
        }
    }
}