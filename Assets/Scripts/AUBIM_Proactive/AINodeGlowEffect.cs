using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// AUBIM 4.0 升级版：节点与常驻 UI 通用金色呼吸特效
/// 使用安全状态机机制，保证被打断时 100% 恢复原色，并支持重复触发
/// </summary>
public class AINodeGlowEffect : MonoBehaviour
{
    private Image _bgImage;
    private Color _originalColor;
    private Color _goldenColor = new Color(1f, 0.8f, 0.2f, 1f); // 灵感金

    private bool _isShuttingDown = false;
    private Coroutine _glowCoroutine; // 新增：缓存当前协程，防止多次调用叠加闪烁

    void OnEnable()
    {
        UserBehaviorSystem.OnEventLogged += HandleUserAction;
    }

    void OnDisable()
    {
        UserBehaviorSystem.OnEventLogged -= HandleUserAction;
        RestoreColor(); // 兜底防御，组件被隐藏/卸载时必定恢复原色
    }

    public void StartGlow(float duration = 60f)
    {
        _bgImage = GetComponent<Image>();
        if (_bgImage == null) _bgImage = GetComponentInChildren<Image>();

        if (_bgImage != null)
        {
            // 如果已经在发光，先停止旧的
            if (_glowCoroutine != null) StopCoroutine(_glowCoroutine);

            _originalColor = _bgImage.color;
            _isShuttingDown = false; // 每次重新发光时，必须重置打断标记！

            _glowCoroutine = StartCoroutine(GlowRoutine(duration));
        }
        else
        {
            Debug.LogWarning("[UI特效] 未找到节点的 Image 背景，无法播放金色呼吸。");
        }
    }

    // ==========================================
    // 【新增核心接口】：供 Copilot 等外部大脑随时主动打断发光
    // ==========================================
    public void StopGlow()
    {
        _isShuttingDown = true;
    }

    private IEnumerator GlowRoutine(float duration)
    {
        float timer = 0f;

        // 只要没被打断且没超时，就持续呼吸
        while (timer < duration && !_isShuttingDown)
        {
            timer += Time.deltaTime;
            float wave = (Mathf.Sin(Time.time * 4f) + 1f) / 2f;

            if (_bgImage != null)
            {
                _bgImage.color = Color.Lerp(_originalColor, _goldenColor, wave);
            }

            yield return null;
        }

        // 循环结束（不论是因为超时，还是外部调用了 StopGlow，或是用户操作打断）
        // 都在协程的生命周期内安全地恢复原色
        RestoreColor();
        _glowCoroutine = null;

        // 【核心修复】：删除了 Destroy(this)！
        // 4.0 的发光组件挂载在常驻按钮上，绝不能自我销毁，否则下次无法再次触发！
    }

    private void HandleUserAction(TelemetryLog log)
    {
        if (_isShuttingDown) return;

        // 忽略 AI 自己触发的系统日志
        if (log.EventType.StartsWith("AI_") || log.EventType.StartsWith("Session"))
            return;

        // 检测到用户进行实质性交互，打破“停滞”状态
        if (log.EventType.StartsWith("Canvas_") ||
            log.EventType.StartsWith("Edit_") ||
            log.EventType.StartsWith("Node_") ||
            log.EventType.StartsWith("Object_") ||
            log.EventType.StartsWith("Article_"))
        {
            // 优雅地修改标记位，让协程自己退出，杜绝死锁与定格
            _isShuttingDown = true;
        }
    }

    private void RestoreColor()
    {
        if (_bgImage != null)
        {
            _bgImage.color = _originalColor;

            // 终极防御：清空底层 CanvasRenderer 的色彩覆写
            // 防止由于鼠标刚好点击在节点上导致的 Button 组件颜色状态被锁死
            _bgImage.CrossFadeColor(_originalColor, 0f, true, true);
        }
    }
}