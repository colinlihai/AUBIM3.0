using UnityEngine;

public class AppWindowSetup : MonoBehaviour
{
    [Header("窗口设置")]
    public int defaultWidth = 1920;
    public int defaultHeight = 1080;

    void Awake()
    {
        // 1. 强制设置为窗口模式
        // FullScreenMode.Windowed: 标准的有边框窗口
        Screen.SetResolution(defaultWidth, defaultHeight, FullScreenMode.Windowed);

        // 2. 允许窗口调整大小 (以防万一 PlayerSettings 没生效)
        // 注意：Screen.fullScreen = false 在旧版 Unity 常用，新版推荐用 SetResolution 的第三个参数

        // 3. 设置帧率 (非游戏应用不需要跑 500帧，省电且防止风扇狂转)
        Application.targetFrameRate = 60;
    }
}