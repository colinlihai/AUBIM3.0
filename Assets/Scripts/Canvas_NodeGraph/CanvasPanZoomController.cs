using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // 必须引用

public class CanvasPanZoomController : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public static CanvasPanZoomController Instance;

    [Header("Components")]
    public RectTransform contentContainer;
    public RectTransform selectionBox;

    [Header("交互阻挡区")]
    public List<RectTransform> blockingUI = new List<RectTransform>();

    [Header("Pan Settings")]
    public float panSpeed = 1f;

    [Header("埋点配置")]
    // [新增] 定义冷却时间变量
    private float _panLogCooldown = 0f;
    private const float LOG_INTERVAL = 1.0f; // 每 1 秒结算一次数据

    // [新增] 累积器：用于记录冷却期间的总变化量
    private float _accumulatedPanDistance = 0f;
    private float _accumulatedZoomDelta = 0f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 0.1f;
    public float minZoom = 0.5f;
    public float maxZoom = 3.0f;
    public float focusZoomLevel = 1.0f;

    private bool _isDragging = false;
    private bool _isBoxSelecting = false;
    private bool _canStartBoxSelection = false; // [修复] 标记是否允许开始框选
    private Vector3 _lastMousePosition;
    private Camera _uiCamera;

    // 框选相关
    private Vector2 _boxStartPos;
    private float _dragThreshold = 10f;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            _uiCamera = canvas.worldCamera;
        }

        if (selectionBox != null) selectionBox.gameObject.SetActive(false);
    }

    void Update()
    {
        HandlePan();
        HandleZoom();
        HandleBoxSelection();
        if (!Input.GetMouseButton(0))
        {
            if (_isBoxSelecting || selectionBox.gameObject.activeSelf)
            {
                ForceCancelBoxSelection();
            }
        }
    }

    public void ForceCancelBoxSelection()
    {
        _isBoxSelecting = false;
        _canStartBoxSelection = false;
        if (selectionBox != null) selectionBox.gameObject.SetActive(false);
    }

    // --- 1. 区分点击目标 ---
    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            _isDragging = true;
            _lastMousePosition = Input.mousePosition;
        }
        else if (eventData.button == PointerEventData.InputButton.Left)
        {
            // [修复Bug 2]：点击节点时，不要触发框选
            // 检查鼠标下方的物体是否是 NodeCard 或其子物体
            GameObject hitObj = eventData.pointerEnter;
            bool hitNode = false;

            if (hitObj != null)
            {
                // 如果点到的物体有 BaseNodeController 组件，或者它的父级有，说明点到了节点
                if (hitObj.GetComponentInParent<BaseNodeController>() != null)
                {
                    hitNode = true;
                }
            }

            if (!hitNode)
            {
                _canStartBoxSelection = true; // 只有点在空地上，才允许框选
                _boxStartPos = Input.mousePosition;
                _isBoxSelecting = false;
            }
            else
            {
                _canStartBoxSelection = false;
            }
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            _isDragging = false;
        }
        else if (eventData.button == PointerEventData.InputButton.Left)
        {
            // 结算框选
            if (_isBoxSelecting)
            {
                ApplyBoxSelection();
                ForceCancelBoxSelection();
            }
            else
            {
                // 如果只是点了下，没拖出框，这里不Reset，留给 OnPointerClick 处理 DeselectAll
                // 只是关闭视觉
                if (selectionBox != null) selectionBox.gameObject.SetActive(false);
            }

            _isBoxSelecting = false;
        }
    }

    // --- 2. 点击事件 ---
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isDragging || _isBoxSelecting) return;

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (eventData.clickCount == 2)
            {
                if (NodeCardManager.Instance != null)
                    NodeCardManager.Instance.CreateNodeCard(eventData.position, _uiCamera);
            }
            else if (eventData.clickCount == 1)
            {
                // [修复] 只有在允许框选（意味着点在空地上）且没有触发框选时，才取消选中
                if (_canStartBoxSelection && NodeCardManager.Instance != null)
                {
                    NodeCardManager.Instance.DeselectAll();
                }
            }
        }
        _canStartBoxSelection = false;
    }

    // --- 3. 框选逻辑 (视觉 + 计算) ---
    private void HandleBoxSelection()
    {
        if (!Input.GetMouseButton(0)) return;
        if (_isDragging) return;
        if (!_canStartBoxSelection) return; // [修复] 如果刚才点的是节点，直接退出

        Vector2 currentMousePos = Input.mousePosition;

        // 阈值检测
        if (!_isBoxSelecting)
        {
            if (Vector2.Distance(currentMousePos, _boxStartPos) > _dragThreshold)
            {
                _isBoxSelecting = true;
                if (selectionBox != null) selectionBox.gameObject.SetActive(true);

                // 开始拖拽选框时，清除之前的选择 (除非按住 Ctrl/Shift)
                bool isMultiKey = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftShift);
                if (!isMultiKey && NodeCardManager.Instance != null)
                {
                    NodeCardManager.Instance.DeselectAll();
                }
            }
        }

        if (_isBoxSelecting && selectionBox != null)
        {
            UpdateSelectionBoxVisual(currentMousePos);
        }
    }

    private void UpdateSelectionBoxVisual(Vector2 currentMousePos)
    {
        float width = currentMousePos.x - _boxStartPos.x;
        float height = currentMousePos.y - _boxStartPos.y;

        selectionBox.sizeDelta = new Vector2(Mathf.Abs(width), Mathf.Abs(height));
        Vector2 center = (_boxStartPos + currentMousePos) / 2f;

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            selectionBox.parent as RectTransform, center, _uiCamera, out localPoint
        );

        selectionBox.localPosition = localPoint;
        selectionBox.pivot = new Vector2(0.5f, 0.5f);
    }

    // [修复Bug 1]：升级为“包围盒重叠检测”
    private void ApplyBoxSelection()
    {
        if (NodeCardManager.Instance == null) return;

        // 1. 获取选框的屏幕空间矩形 (Min/Max)
        Vector2 min = Vector2.Min(_boxStartPos, Input.mousePosition);
        Vector2 max = Vector2.Max(_boxStartPos, Input.mousePosition);
        Rect selectionRect = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);

        var allNodes = NodeCardManager.Instance.GetAllNodes();

        foreach (var node in allNodes)
        {
            if (node == null) continue;

            // 获取节点可视部分的 RectTransform (如果有 visual 优先用 visual，否则用自身)
            RectTransform nodeRect = node.visual != null ?
                node.visual.GetComponent<RectTransform>() :
                node.GetComponent<RectTransform>();

            // 2. 计算节点的屏幕空间包围盒 (AABB)
            Vector3[] corners = new Vector3[4];
            nodeRect.GetWorldCorners(corners);

            float nodeMinX = float.MaxValue, nodeMaxX = float.MinValue;
            float nodeMinY = float.MaxValue, nodeMaxY = float.MinValue;

            foreach (var corner in corners)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(_uiCamera, corner);
                if (screenPoint.x < nodeMinX) nodeMinX = screenPoint.x;
                if (screenPoint.x > nodeMaxX) nodeMaxX = screenPoint.x;
                if (screenPoint.y < nodeMinY) nodeMinY = screenPoint.y;
                if (screenPoint.y > nodeMaxY) nodeMaxY = screenPoint.y;
            }

            Rect nodeScreenRect = new Rect(nodeMinX, nodeMinY, nodeMaxX - nodeMinX, nodeMaxY - nodeMinY);

            // 3. 检测两个矩形是否重叠 (Overlaps)
            // allowInverse: false (因为我们已经规范化了 min/max)
            if (selectionRect.Overlaps(nodeScreenRect))
            {
                NodeCardManager.Instance.SelectNode(node, true); // true = 累加选中
            }
        }
    }

    // --- 平移 & 缩放 (保持不变) ---
    private void HandlePan()
    {
        if (!_isDragging) return;
        Vector3 currentMousePos = Input.mousePosition;
        Vector3 diff = currentMousePos - _lastMousePosition;
        if (diff.sqrMagnitude < 0.1f) return;

        Vector2 localCursor;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(contentContainer.parent as RectTransform, currentMousePos, _uiCamera, out localCursor)) return;
        Vector2 lastLocalCursor;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(contentContainer.parent as RectTransform, _lastMousePosition, _uiCamera, out lastLocalCursor)) return;

        // 计算这一帧的位移向量
        Vector2 moveVector = localCursor - lastLocalCursor;

        // 执行移动
        contentContainer.anchoredPosition += moveVector;
        _lastMousePosition = currentMousePos;

        // --- [埋点逻辑开始] ---

        // 1. 累积这一帧的物理距离 (Value 的核心来源)
        _accumulatedPanDistance += moveVector.magnitude;

        // 2. 检查是否达到结算时间 (且有实质移动)
        // 设定一个累积阈值 (比如 50px)，防止用户手抖导致记录垃圾数据
        if (Time.time > _panLogCooldown && _accumulatedPanDistance > 50f)
        {
            if (UserBehaviorSystem.Instance != null)
            {
                UserBehaviorSystem.Instance.LogEvent(
                    BehaviorEventType.View_PanZoom,
                    targetID: "Camera",
                    info: "Pan",
                    value: _accumulatedPanDistance // 记录过去1秒总共移了多远
                );
            }

            // 3. 重置累积器和计时器
            _accumulatedPanDistance = 0f;
            _panLogCooldown = Time.time + LOG_INTERVAL;
        }
    }

    private void HandleZoom()
    {
        if (IsPointerOverBlockingUI()) return;

        float scrollInput = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scrollInput) < 0.001f) return;

        float currentScale = contentContainer.localScale.x;
        float targetScale = Mathf.Clamp(currentScale + scrollInput * zoomSpeed, minZoom, maxZoom);

        // 计算这一帧的缩放差
        float frameScaleDelta = Mathf.Abs(targetScale - currentScale);
        if (frameScaleDelta < 0.001f) return;

        Vector2 mouseLocalPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(contentContainer, Input.mousePosition, _uiCamera, out mouseLocalPoint))
        {
            contentContainer.localScale = Vector3.one * targetScale;
            contentContainer.anchoredPosition -= mouseLocalPoint * (targetScale - currentScale);
        }

        // --- [埋点逻辑开始] ---

        // 1. 累积缩放变化量
        _accumulatedZoomDelta += frameScaleDelta;

        // 2. 检查结算 (复用同一个冷却计时器，或者单独开一个也可以，复用比较省事)
        // 缩放阈值设为 0.1，因为缩放数值通常比较小
        if (Time.time > _panLogCooldown && _accumulatedZoomDelta > 0.1f)
        {
            if (UserBehaviorSystem.Instance != null)
            {
                UserBehaviorSystem.Instance.LogEvent(
                    BehaviorEventType.View_PanZoom,
                    targetID: "Camera",
                    info: "Zoom",
                    value: _accumulatedZoomDelta // 记录过去1秒总共缩放了多少
                );
            }

            // 3. 重置
            _accumulatedZoomDelta = 0f;
            _panLogCooldown = Time.time + LOG_INTERVAL;
        }
    }

    /// <summary>
    /// 检测鼠标是否在阻挡交互的 UI 上（忽略画布背景和节点卡片）
    /// </summary>
    private bool IsPointerOverBlockingUI()
    {
        if (EventSystem.current == null) return false;

        // 1. 发射射线检测所有 UI
        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = Input.mousePosition;
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);

        // 2. 遍历结果进行筛选
        foreach (var result in results)
        {
            // A. 如果点到的是自己（挂载此脚本的背景层），不算阻挡，继续找
            if (result.gameObject == this.gameObject) continue;

            // B. 如果点到的是 Content 容器本身，不算阻挡
            if (contentContainer != null && result.gameObject == contentContainer.gameObject) continue;

            // C. 如果点到的是思维导图节点（NodeCard），不算阻挡，允许在节点上缩放
            if (result.gameObject.GetComponentInParent<BaseNodeController>() != null) continue;

            // D. 如果是其他 UI（比如 ChatPanel、按钮、输入框），视为阻挡
            return true;
        }

        // 如果遍历完所有层级，没有发现阻挡物，则允许缩放
        return false;
    }

    public void FocusOn(Vector2 targetLocalPos)
    {
        float targetScale = Mathf.Clamp(focusZoomLevel, minZoom, maxZoom);
        contentContainer.localScale = Vector3.one * targetScale;
        contentContainer.anchoredPosition = -targetLocalPos * targetScale;
    }
}