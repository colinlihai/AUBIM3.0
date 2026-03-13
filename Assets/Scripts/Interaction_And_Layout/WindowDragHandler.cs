using UnityEngine;
using UnityEngine.EventSystems; // 必须引用，用于处理 UI 事件

public class WindowDragHandler : MonoBehaviour, IDragHandler, IBeginDragHandler
{
    [Header("设置")]
    [Tooltip("要移动的整个窗口对象，如果不填则默认移动父物体")]
    public RectTransform targetWindow;

    private Canvas _canvas;

    void Start()
    {
        // 1. 获取所在的 Canvas (用于计算缩放比例)
        _canvas = GetComponentInParent<Canvas>();

        // 2. 如果没手动赋值目标窗口，自动找父物体
        if (targetWindow == null)
        {
            // 假设 DragArea 是 ArticleModal 的直接子物体
            targetWindow = transform.parent.GetComponent<RectTransform>();
        }
    }

    // 开始拖拽时调用一次
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (targetWindow == null) return;

        // [可选] 点击时把窗口提到最上层 (显示在最前面)
        targetWindow.SetAsLastSibling();
    }

    // 拖拽过程中每帧调用
    public void OnDrag(PointerEventData eventData)
    {
        if (targetWindow == null || _canvas == null) return;

        // ★★★ 核心逻辑 ★★★
        // eventData.delta 是鼠标这一帧移动的像素距离
        // 除以 scaleFactor 是为了让移动速度适配 Canvas 的分辨率缩放，否则在不同分辨率下拖拽速度会不对
        targetWindow.anchoredPosition += eventData.delta / _canvas.scaleFactor;
    }
}