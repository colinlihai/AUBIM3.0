using UnityEngine;

public class ThemeManager : MonoBehaviour
{
    public static ThemeManager Instance;

    [Header("当前使用的主题")]
    public AppTheme currentTheme;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // 可以在这里添加 Update 逻辑，如果检测到 currentTheme 变了，广播事件通知 UI 刷新（进阶做法）
}