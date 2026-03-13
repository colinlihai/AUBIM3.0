using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class AppExitManager : MonoBehaviour
{
    [Header("UI 引用")]
    public GameObject exitPanel; // 指向 ExitConfirmationPanel
    public Button confirmBtn;
    public Button cancelBtn;

    [Header("冲突检测 (可选)")]
    // 如果有其他弹窗（如文章窗口）打开时，按 ESC 应该是关闭那个窗口而不是退出
    // 这里可以引用 ArticleGenerator 来做判断
    public ArticleGenerator articleGen;

    void Start()
    {
        // 1. 绑定按钮事件
        if (confirmBtn != null) confirmBtn.onClick.AddListener(OnConfirmQuit);
        if (cancelBtn != null) cancelBtn.onClick.AddListener(OnCancel);

        // 2. 确保初始隐藏
        if (exitPanel != null) exitPanel.SetActive(false);
    }

    void Update()
    {
        // 监听 ESC 键
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            HandleEscInput();
        }
    }

    private void HandleEscInput()
    {
        // 1. 如果退出弹窗已经打开 -> 关闭它 (相当于取消)
        if (exitPanel != null && exitPanel.activeSelf)
        {
            OnCancel();
            return;
        }

        // 2. [优化体验] 如果文章弹窗正打开着 -> 按 ESC 应该是关闭文章弹窗
        // 防止用户只是想关文章，结果弹出了退出警告
        if (articleGen != null && articleGen.articleModal != null && articleGen.articleModal.activeSelf)
        {
            articleGen.CloseModal(); // 调用你 ArticleGenerator 里的关闭方法
            return;
        }

        // 3. [优化体验] 如果 AI 指令条打开着 (如果你做了那个功能)
        // ... (同理处理)

        // 4. 如果没别的遮挡，则显示退出确认框
        ShowExitConfirmation();
    }

    public void ShowExitConfirmation()
    {
        if (exitPanel != null)
        {
            exitPanel.SetActive(true);
            // 这里可以加一个简单的 DoTween 动画，比如从 0 缩放到 1
        }
    }

    private void OnCancel()
    {
        if (exitPanel != null) exitPanel.SetActive(false);
    }

    private void OnConfirmQuit()
    {
        // A. [关键] 退出前强制保存！
        // 这样用户即使忘记保存直接点退出，数据也是安全的
        if (SaveSystem.Instance != null)
        {
            Debug.Log("[System] 退出前自动保存...");
            SaveSystem.Instance.SaveProject("AutoSave_OnExit");
        }

        Debug.Log("正在退出软件...");

        // B. 执行退出
#if UNITY_EDITOR
        // 在编辑器模式下停止运行
        EditorApplication.isPlaying = false;
#else
            // 在打包版本中退出程序
            Application.Quit();
#endif
    }
}