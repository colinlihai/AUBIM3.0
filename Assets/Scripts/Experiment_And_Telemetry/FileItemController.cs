using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class FileItemController : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI 组件引用")]
    public TMP_Text fileNameText;      // 显示存档名
    public TMP_Text timeText;          // 显示时间
    public Button deleteBtn;           // 删除按钮
    public Image backgroundImage;      // 背景图 (用于高亮选中)

    // 内部数据
    private string _fullFileName;
    private SaveLoadMenu _menu;        // 持有菜单的引用，用于回调
    private bool _isSelected = false;

    // 初始化方法 (由 SaveLoadMenu 调用)
    public void Init(string fileName, string time, SaveLoadMenu menu)
    {
        _fullFileName = fileName;
        _menu = menu;

        if (fileNameText) fileNameText.text = fileName;
        if (timeText) timeText.text = time;

        // 绑定删除按钮事件
        if (deleteBtn)
        {
            deleteBtn.onClick.RemoveAllListeners();
            // 点击删除时，调用 Menu 的删除逻辑
            deleteBtn.onClick.AddListener(() =>
            {
                Debug.Log($"[FileItem] 删除了按钮被点击: {fileName}"); // 2. 确认点击生效了
                if (_menu != null)
                {
                    _menu.OnDeleteFile(_fullFileName);
                }
                else
                {
                    Debug.LogError("Menu 引用丢失！");
                }
            });
        }

        UpdateVisuals();
    }

    // 实现点击接口
    public void OnPointerClick(PointerEventData eventData)
    {
        // 1. 通知 Menu：我被选中了
        if (_menu != null)
        {
            _menu.OnItemSelected(this, _fullFileName);
        }

        // 2. 检测双击 (Double Click) -> 直接读取
        if (eventData.clickCount == 2)
        {
            if (_menu != null) _menu.OnLoadRequested(_fullFileName);
        }
    }

    // 鼠标悬停效果
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_isSelected && backgroundImage != null)
        {
            var theme = ThemeManager.Instance != null ? ThemeManager.Instance.currentTheme : null;
            if (theme != null)
            {
                backgroundImage.color = theme.fileItemHover;
            }
            else
            {
                backgroundImage.color = new Color(1f, 1f, 1f, 0.1f); // 兜底
            }
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isSelected && backgroundImage != null)
        {
            var theme = ThemeManager.Instance != null ? ThemeManager.Instance.currentTheme : null;
            if (theme != null)
            {
                backgroundImage.color = theme.fileItemNormal;
            }
            else
            {
                backgroundImage.color = new Color(0, 0, 0, 0); // 兜底
            }
        }
    }

    // 设置选中状态 (由 Menu 控制)
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (backgroundImage == null) return;

        var theme = ThemeManager.Instance != null ? ThemeManager.Instance.currentTheme : null;

        // 默认透明
        Color targetColor = new Color(0, 0, 0, 0);

        if (_isSelected)
        {
            // 选中状态
            if (theme != null) targetColor = theme.fileItemSelected;
            else targetColor = new Color(0.2f, 0.6f, 1f, 0.3f); // 兜底
        }
        else
        {
            // 普通状态 (未选中)
            if (theme != null) targetColor = theme.fileItemNormal;
            // 兜底默认是透明，不用变
        }

        backgroundImage.color = targetColor;
    }
}