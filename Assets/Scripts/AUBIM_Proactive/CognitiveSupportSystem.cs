using UnityEngine;
using UnityEngine.UI;

public class CognitiveSupportSystem : MonoBehaviour
{
    public static CognitiveSupportSystem Instance;

    [Header("UI 按钮引用 (仅作展示容器)")]
    public Button socraticBtn;
    public Button counterBtn;
    public Button elaborateBtn;
    public GameObject buttonContainer;

    public Camera uiCamera;

    [Tooltip("距离节点顶部的偏移量 (像素)")]
    public float verticalOffset = 20f;
    public bool scaleWithZoom = false;

    // --- 优化点1：缓存目标引用，避免每帧 GetComponent ---
    private BaseNodeController _targetNode;
    private RectTransform _targetRect;

    private RectTransform _containerRect;
    private RectTransform _parentRect;

    void Awake()
    {
        Instance = this;
        _containerRect = buttonContainer.GetComponent<RectTransform>();
        _parentRect = _containerRect.parent as RectTransform;

        if (buttonContainer != null) buttonContainer.SetActive(false);
    }

    void Start()
    {
        if (uiCamera == null) uiCamera = Camera.main;
    }

    // --- 优化点2：使用 LateUpdate 替代 Update，避免 UI 跟随抖动 ---
    void LateUpdate()
    {
        // 1. 轻量级的状态检查
        CheckSelectionState();

        // 2. 优化点3：仅在有目标时才进行耗时的矩阵计算
        if (_targetNode != null && buttonContainer.activeSelf)
        {
            UpdatePosition();
        }
    }

    private void CheckSelectionState()
    {
        if (NodeCardManager.Instance == null) return;
        var selected = NodeCardManager.Instance.GetFirstSelected();

        // 当选中的是有效的 NodeCard 时
        if (selected != null && selected.cardType == CardType.NodeCard)
        {
            // 如果目标发生变化，更新缓存
            if (_targetNode != selected)
            {
                _targetNode = selected;
                _targetRect = _targetNode.GetComponent<RectTransform>(); // 仅在切换时获取一次
                buttonContainer.SetActive(true);
            }
        }
        else
        {
            // 如果没有选中任何有效节点，清理缓存并隐藏 UI
            if (_targetNode != null)
            {
                _targetNode = null;
                _targetRect = null;
                buttonContainer.SetActive(false);
            }
        }
    }

    private void UpdatePosition()
    {
        if (_targetRect == null || uiCamera == null) return;

        // 获取画板纯净的缩放值 (Zoom Level)
        float zoomLevel = 1f;
        if (CanvasPanZoomController.Instance != null && CanvasPanZoomController.Instance.contentContainer != null)
        {
            zoomLevel = CanvasPanZoomController.Instance.contentContainer.localScale.y;
        }

        // 控制尺寸
        _containerRect.localScale = scaleWithZoom ? Vector3.one * zoomLevel : Vector3.one;

        // 精准的世界坐标提取 (这部分矩阵运算现在只在 UI 显示时才会执行)
        Vector3[] corners = new Vector3[4];
        _targetRect.GetWorldCorners(corners);

        Vector3 topCenterWorld = (corners[1] + corners[2]) / 2f;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCamera, topCenterWorld);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, screenPos, uiCamera, out Vector2 localPoint))
        {
            float currentOffset = scaleWithZoom ? (verticalOffset * zoomLevel) : verticalOffset;
            _containerRect.anchoredPosition = localPoint + new Vector2(0, currentOffset);
        }
    }
}