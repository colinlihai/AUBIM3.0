using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// AUBIM 3.0 节点专属金色呼吸特效
/// 使用安全状态机机制，保证被打断时 100% 恢复原色
/// </summary>
public class AINodeGlowEffect : MonoBehaviour
{
    private Image _bgImage;
    private Color _originalColor;
    private Color _goldenColor = new Color(1f, 0.8f, 0.2f, 1f); // 灵感金

    // 核心修复：状态机标记
    private bool _isShuttingDown = false;

    void OnEnable()
    {
        UserBehaviorSystem.OnEventLogged += HandleUserAction;
    }

    void OnDisable()
    {
        UserBehaviorSystem.OnEventLogged -= HandleUserAction;
        RestoreColor(); // 兜底防御，组件被卸载时必定恢复原色
    }

    public void StartGlow(float duration = 60f)
    {
        _bgImage = GetComponent<Image>();
        if (_bgImage == null) _bgImage = GetComponentInChildren<Image>();

        if (_bgImage != null)
        {
            _originalColor = _bgImage.color;
            StartCoroutine(GlowRoutine(duration));
        }
        else
        {
            Debug.LogWarning("[UI特效] 未找到节点的 Image 背景，无法播放金色呼吸。");
        }
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

        // 循环结束（不论是因为超时，还是因为用户操作导致 _isShuttingDown 为 true）
        // 都在协程的生命周期内安全地恢复原色
        RestoreColor();

        // 延迟一帧销毁自身，避免影响当前帧的 UI 渲染
        Destroy(this);
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
            // 【核心修复】：不再使用暴力的 StopCoroutine
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