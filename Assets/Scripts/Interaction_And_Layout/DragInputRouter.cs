using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(InteractionDetector))]
public class DragInputRouter : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("拖拽目标设置")]
    public Transform targetRoot;
    [Header("Proxy Settings")]
    public Transform proxyLayer;   // 替身生成的父节点 (Canvas)

    private RectTransform _targetRect;
    private RectTransform _selfVisualRect;
    private Vector2 _originalPosition;
    private Transform _originalParent;
    private int _originalSiblingIndex;

    private BaseNodeController _myController;
    private BaseNodeController _currentFeedbackSibling;
    private BaseNodeController _currentFeedbackParent;

    private struct DetachInfo
    {
        public BaseNodeController originalParent;
        public int originalSiblingIndex;
    }

    private Dictionary<BaseNodeController, DetachInfo> _detachedStates = new Dictionary<BaseNodeController, DetachInfo>();
    private bool _forceSnapBack = false;
    private InteractionDetector _detector;

    // --- 替身战队管理 ---
    private class ProxyData
    {
        public RectTransform proxyRect;       // 替身物体
        public BaseNodeController sourceNode; // 真身控制器
        public Vector3 dragOffset;            // 相对于鼠标(或Leader)的偏移
    }

    private List<ProxyData> _activeProxies = new List<ProxyData>();

    void Awake()
    {
        if (targetRoot == null) targetRoot = this.transform;
        _targetRect = targetRoot.GetComponent<RectTransform>();
        _myController = targetRoot.GetComponent<BaseNodeController>();
        _detector = GetComponent<InteractionDetector>();
    }

    void Start()
    {
        if (_myController != null && _myController.visual != null)
            _selfVisualRect = _myController.visual.GetComponent<RectTransform>();
        else
            _selfVisualRect = _targetRect;

        if (proxyLayer == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) proxyLayer = canvas.transform;
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (CanvasPanZoomController.Instance != null)
        {
            CanvasPanZoomController.Instance.ForceCancelBoxSelection();
        }

        _originalParent = targetRoot.parent;
        _originalPosition = _targetRect.anchoredPosition;
        _originalSiblingIndex = targetRoot.GetSiblingIndex();

        _forceSnapBack = false;
        _detachedStates.Clear();

        var visual = GetComponentInParent<CoreBodyVisual>();
        if (visual != null && visual.IsEditing) return;

        // 3.0 架构：统一使用替身模式 (移除原有的 _isProxyMode 判断)
        CreateProxySquad(eventData);

        // 全员开启幽灵模式
        foreach (var data in _activeProxies)
        {
            if (data.sourceNode != null) data.sourceNode.SetGhostMode(true);
        }
    }

    public void RequestSnapBack() { _forceSnapBack = true; }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        var visual = GetComponentInParent<CoreBodyVisual>();
        if (visual != null && visual.IsEditing) return;

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
            proxyLayer as RectTransform, eventData.position, eventData.pressEventCamera, out Vector3 worldMousePos))
        {
            // 1. 移动战队
            foreach (var data in _activeProxies)
            {
                if (data.proxyRect != null)
                {
                    data.proxyRect.position = worldMousePos + data.dragOffset;
                }
            }

            // =========================================================
            // 【意图识别】：检测是否悬停在成文区，呼出幽灵光标并立即逻辑归位
            // =========================================================
            bool hoveringOnArticle = false;
            if (ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy)
            {
                RectTransform articleRect = ArticleGenerator.Instance.mainBodyInput.GetComponent<RectTransform>();
                if (RectTransformUtility.RectangleContainsScreenPoint(articleRect, eventData.position, eventData.pressEventCamera))
                {
                    hoveringOnArticle = true;
                    ArticleGenerator.Instance.UpdateDragDropFeedback(eventData.position, eventData.pressEventCamera);
                }
            }

            // 【满足你的绝妙设计】：一旦鼠标进入正文区，立刻归位！
            if (hoveringOnArticle)
            {
                ForceRestoreOriginalState(); // 后台瞬间完美归位
                ClearAllFeedback();          // 清除导图区的绿线等排版反馈
                return;                      // 提前退出！不再执行下方的导图断开和重组检测
            }
            else
            {
                if (ArticleGenerator.Instance != null) ArticleGenerator.Instance.ClearDragDropFeedback();
            }

            bool isMultiSelect = _activeProxies.Count > 1;

            if (_detector != null)
            {
                Vector2 threshold = _detector.detachThreshold;

                foreach (var data in _activeProxies)
                {
                    if (!isMultiSelect && data.sourceNode == _myController) continue;

                    if (data.sourceNode != null && data.sourceNode.parentNode != null)
                    {
                        var parent = data.sourceNode.parentNode;
                        // 如果我的父节点也在这批拖拽列表里，说明我们在整体搬家，绝对不要断开连线！
                        bool isParentAlsoDragging = _activeProxies.Exists(p => p.sourceNode == parent);
                        if (isParentAlsoDragging) continue;
                        if (parent != null)
                        {
                            Vector3 localPos = parent.transform.InverseTransformPoint(data.proxyRect.position);
                            Vector2 currentAnchored = (Vector2)localPos;
                            Vector2 originalAnchored = data.sourceNode.GetComponent<RectTransform>().anchoredPosition;
                            Vector2 dragDelta = currentAnchored - originalAnchored;

                            bool shouldDetach = Mathf.Abs(dragDelta.x) > threshold.x;

                            if (shouldDetach)
                            {
                                if (NodeLinkManager.Instance != null)
                                {
                                    if (!_detachedStates.ContainsKey(data.sourceNode))
                                    {
                                        _detachedStates.Add(data.sourceNode, new DetachInfo
                                        {
                                            originalParent = parent,
                                            originalSiblingIndex = data.sourceNode.transform.GetSiblingIndex()
                                        });
                                    }
                                }
                                NodeLinkManager.Instance.BreakConnection(data.sourceNode);
                            }
                        }
                    }
                }

                // 2. 环境检测 (Leader)
                var leaderData = _activeProxies.Find(x => x.sourceNode == _myController);
                if (leaderData != null && _myController != null)
                {
                    Vector2 currentSimulatedAnchoredPos = _originalPosition;
                    if (targetRoot.parent != null)
                    {
                        Vector3 localPos = targetRoot.parent.InverseTransformPoint(leaderData.proxyRect.position);
                        currentSimulatedAnchoredPos = (Vector2)localPos;
                    }
                    Vector2 dragDelta = currentSimulatedAnchoredPos - _originalPosition;

                    bool shouldDetach = _detector.Detect(eventData, _myController, leaderData.proxyRect.position, dragDelta);

                    var potentialParent = _detector.PotentialParent;

                    // A. 处理父节点反馈
                    if (_currentFeedbackParent != potentialParent)
                    {
                        if (_currentFeedbackParent != null && _currentFeedbackParent.visual != null)
                            _currentFeedbackParent.visual.SetParentTargetState(false);
                        if (potentialParent != null && potentialParent.visual != null)
                            potentialParent.visual.SetParentTargetState(true);
                        _currentFeedbackParent = potentialParent;
                    }

                    // B. 处理子节点反馈
                    if (_myController.visual != null)
                    {
                        _myController.visual.SetChildAdsorbState(potentialParent != null);
                    }

                    // C. 处理插入线
                    if (DropFeedbackSystem.Instance != null)
                    {
                        if (shouldDetach || _detector.PotentialSibling == null)
                        {
                            DropFeedbackSystem.Instance.HideAll();
                        }
                    }

                    // D. 处理逻辑分离
                    if (shouldDetach)
                    {
                        var parent = _myController.parentNode;
                        if (NodeLinkManager.Instance != null)
                        {
                            if (parent != null && !_detachedStates.ContainsKey(_myController))
                            {
                                _detachedStates.Add(_myController, new DetachInfo
                                {
                                    originalParent = parent,
                                    originalSiblingIndex = _myController.transform.GetSiblingIndex()
                                });
                            }
                            NodeLinkManager.Instance.BreakConnection(_myController);
                        }
                    }

                    var potentialSibling = _detector.PotentialSibling;
                    if (_currentFeedbackSibling != potentialSibling)
                    {
                        if (_currentFeedbackSibling != null && _currentFeedbackSibling.visual != null)
                            _currentFeedbackSibling.visual.SetReorderTargetState(false);
                        if (potentialSibling != null && potentialSibling.visual != null)
                            potentialSibling.visual.SetReorderTargetState(true);
                        _currentFeedbackSibling = potentialSibling;
                    }
                }
            }
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        ClearAllFeedback();

        // =========================================================
        // 检测是否拖拽到了成文区的正文输入框中
        // =========================================================
        bool droppedOnArticle = false;
        if (ArticleGenerator.Instance != null && ArticleGenerator.Instance.articleModal.activeInHierarchy)
        {
            RectTransform articleRect = ArticleGenerator.Instance.mainBodyInput.GetComponent<RectTransform>();
            // 使用屏幕坐标和相机进行碰撞检测
            if (RectTransformUtility.RectangleContainsScreenPoint(articleRect, eventData.position, eventData.pressEventCamera))
            {
                droppedOnArticle = true;
                _forceSnapBack = true; // 【关键】：把节点原路弹回画布，因为这相当于“提取素材”，而不是“搬家”
            }
        }

        // 缓存 Leader 最终位置
        Vector3 leaderFinalWorldPos = _originalPosition;
        var leaderData = _activeProxies.Find(x => x.sourceNode == _myController);
        if (leaderData != null && leaderData.proxyRect != null)
        {
            leaderFinalWorldPos = leaderData.proxyRect.position;
        }

        // 1. 销毁战队 & 恢复幽灵模式
        foreach (var data in _activeProxies)
        {
            if (data.proxyRect != null) Destroy(data.proxyRect.gameObject);
            if (data.sourceNode != null) data.sourceNode.SetGhostMode(false);
        }
        _activeProxies.Clear();

        // 【极简结算】：如果是投入成文区，触发生成逻辑并立刻结束
        if (droppedOnArticle)
        {
            ArticleGenerator.Instance.ClearDragDropFeedback();
            ArticleGenerator.Instance.HandleNodeDropped(eventData.position, eventData.pressEventCamera, _myController);

            // 兜底归位（如果在 OnDrag 已经完美归位，这里相当于最后一道保险）
            ForceRestoreOriginalState();

            return; // 提前退出，禁止触发画布移动！
        }

        // 兜底：即使不是投进文章区，拖拽结束也隐藏幽灵光标
        if (ArticleGenerator.Instance != null) ArticleGenerator.Instance.ClearDragDropFeedback();

        // 2. 结算交互结果
        if (_forceSnapBack)
        {
            _targetRect.anchoredPosition = _originalPosition;
            targetRoot.SetSiblingIndex(_originalSiblingIndex);

            if (NodeLinkManager.Instance != null)
            {
                foreach (var pair in _detachedStates)
                {
                    var node = pair.Key;
                    var info = pair.Value;
                    if (node != null && info.originalParent != null)
                    {
                        NodeLinkManager.Instance.CreateConnection(info.originalParent, node);
                        node.transform.SetSiblingIndex(info.originalSiblingIndex);
                        NodeLinkManager.Instance.ReorderChildren(info.originalParent);
                    }
                }
            }
            _detachedStates.Clear();
            if (AutoLayoutSystem.Instance != null) AutoLayoutSystem.Instance.RefreshLayout(_myController);
        }
        else
        {
            bool wasDetached = _detachedStates.ContainsKey(_myController);
            Debug.Log($"<color=magenta>[Drag Router 诊断]</color> 拖拽松手！节点 {_myController.NodeID} 刚才是否发生了分离? wasDetached = {wasDetached}");
            if (CommandManager.Instance != null && _detachedStates.Count > 0)
            {
                foreach (var pair in _detachedStates)
                {
                    var node = pair.Key;
                    var info = pair.Value;
                    // 无条件记录断开命令（因为它在 OnDrag 时已经被真实斩断了，此时入栈是最安全的）
                    var cmd = new ConnectionCommand(info.originalParent, node, false, info.originalSiblingIndex);
                    CommandManager.Instance.ExecuteCommand(cmd);
                }
            }
            _detachedStates.Clear();
            bool interactionApplied = ApplyDragResult();

            if (!interactionApplied)
            {
                // 自由移动 (多选搬运)
                if (_myController.parentNode == null)
                {
                    float w = _targetRect.rect.width;
                    float h = _targetRect.rect.height;
                    Vector3 centerLocalPos = new Vector3(w * 0.5f, -h * 0.5f, 0);
                    Vector3 centerWorldOffset = _targetRect.TransformVector(centerLocalPos);

                    Vector3 leaderTargetPos = leaderFinalWorldPos - centerWorldOffset;
                    Vector3 worldMoveDelta = leaderTargetPos - _targetRect.position;

                    _targetRect.position = leaderTargetPos;
                    if (AutoLayoutSystem.Instance != null) AutoLayoutSystem.Instance.RefreshLayout(_myController);

                    if (NodeCardManager.Instance != null)
                    {
                        var selected = NodeCardManager.Instance.GetSelectedNodes();
                        foreach (var node in selected)
                        {
                            if (node == _myController) continue;
                            if (node.parentNode == null)
                            {
                                node.transform.position += worldMoveDelta;
                                if (AutoLayoutSystem.Instance != null) AutoLayoutSystem.Instance.RefreshLayout(node);
                            }
                        }
                    }

                    if (CommandManager.Instance != null)
                    {
                        Vector2 finalPos = _targetRect.anchoredPosition;
                        if (Vector2.Distance(finalPos, _originalPosition) > 1f)
                        {
                            var cmd = new MoveNodeCommand(_myController, _originalPosition, finalPos, wasDetached);
                            CommandManager.Instance.ExecuteCommand(cmd);
                        }
                    }
                }
                else
                {
                    _targetRect.anchoredPosition = _originalPosition;
                }
            }
        }

        // 3. 记录空间排布命令
        if (CommandManager.Instance != null && targetRoot.parent != _originalParent)
        {
            var cmd = new ReparentUICommand(
                _myController,
                _originalParent,
                targetRoot.parent,
                _originalSiblingIndex,
                targetRoot.GetSiblingIndex()
            );
            CommandManager.Instance.ExecuteCommand(cmd);
        }
        else if (CommandManager.Instance != null && targetRoot.parent == _originalParent)
        {
            int currentIndex = targetRoot.GetSiblingIndex();
            if (currentIndex != _originalSiblingIndex)
            {
                var cmd = new ReorderSiblingCommand(_myController, _originalSiblingIndex, currentIndex);
                CommandManager.Instance.ExecuteCommand(cmd);
            }
        }
    }

    private void ClearAllFeedback()
    {
        if (_myController != null && _myController.visual != null)
            _myController.visual.SetChildAdsorbState(false);

        if (_currentFeedbackParent != null && _currentFeedbackParent.visual != null)
            _currentFeedbackParent.visual.SetParentTargetState(false);
        _currentFeedbackParent = null;

        if (DropFeedbackSystem.Instance != null)
            DropFeedbackSystem.Instance.HideAll();

        if (_currentFeedbackSibling != null && _currentFeedbackSibling.visual != null)
            _currentFeedbackSibling.visual.SetReorderTargetState(false);
        _currentFeedbackSibling = null;
    }

    private void CreateProxySquad(PointerEventData eventData)
    {
        _activeProxies.Clear();
        if (proxyLayer == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) proxyLayer = canvas.transform;
        }

        List<BaseNodeController> nodesToDrag = new List<BaseNodeController>();

        if (NodeCardManager.Instance != null)
        {
            var selected = NodeCardManager.Instance.GetSelectedNodes();
            if (selected.Contains(_myController))
            {
                nodesToDrag.AddRange(selected);
            }
            else
            {
                nodesToDrag.Add(_myController);
            }
        }
        else
        {
            nodesToDrag.Add(_myController);
        }

        foreach (var node in nodesToDrag)
        {
            GameObject sourceObj = (node.visual != null) ? node.visual.gameObject : node.gameObject;
            GameObject proxyObj = Instantiate(sourceObj, proxyLayer);
            RectTransform proxyRect = proxyObj.GetComponent<RectTransform>();

            proxyRect.pivot = new Vector2(0.5f, 0.5f);
            proxyRect.anchorMin = new Vector2(0.5f, 0.5f);
            proxyRect.anchorMax = new Vector2(0.5f, 0.5f);

            Camera cam = eventData.pressEventCamera;
            Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, node.transform.position);
            Vector3 projectedWorldPos;
            RectTransformUtility.ScreenPointToWorldPointInRectangle(proxyLayer as RectTransform, screenPos, cam, out projectedWorldPos);

            proxyRect.position = projectedWorldPos;
            proxyRect.rotation = node.transform.rotation;

            RectTransform nodeRect = node.GetComponent<RectTransform>();
            proxyRect.sizeDelta = nodeRect.rect.size;

            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;
            float distTarget = Vector3.Dot(node.transform.position - camPos, camFwd);
            float distProxy = Vector3.Dot(projectedWorldPos - camPos, camFwd);
            float depthFactor = (distTarget == 0) ? 1f : (distProxy / distTarget);

            Vector3 parentScale = proxyLayer.lossyScale;
            Vector3 targetScale = node.transform.lossyScale;
            float sX = (parentScale.x == 0) ? 1 : targetScale.x / parentScale.x;
            float sY = (parentScale.y == 0) ? 1 : targetScale.y / parentScale.y;

            proxyRect.localScale = new Vector3(sX * depthFactor, sY * depthFactor, 1f);

            CanvasGroup cg = proxyObj.GetComponent<CanvasGroup>();
            if (cg == null) cg = proxyObj.AddComponent<CanvasGroup>();
            cg.alpha = 0.2f;
            cg.blocksRaycasts = false;
            cg.interactable = false;
            foreach (var g in proxyObj.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;

            var router = proxyObj.GetComponent<DragInputRouter>();
            if (router != null) Destroy(router);
            var controller = proxyObj.GetComponent<BaseNodeController>();
            if (controller != null) Destroy(controller);
            var detector = proxyObj.GetComponent<InteractionDetector>();
            if (detector != null) Destroy(detector);

            ProxyData data = new ProxyData();
            data.proxyRect = proxyRect;
            data.sourceNode = node;

            _activeProxies.Add(data);
        }

        var leaderData = _activeProxies.Find(x => x.sourceNode == _myController);
        Vector3 leaderBasePos = (leaderData != null) ? leaderData.proxyRect.position : Vector3.zero;

        foreach (var data in _activeProxies)
        {
            if (data == leaderData)
                data.dragOffset = Vector3.zero;
            else
                data.dragOffset = data.proxyRect.position - leaderBasePos;
        }
    }

    private bool ApplyDragResult()
    {
        if (_detector == null) return false;
        var potentialParent = _detector.PotentialParent;
        var potentialSibling = _detector.PotentialSibling;

        if (potentialParent != null)
        {
            if (NodeLinkManager.Instance != null)
            {
                List<BaseNodeController> nodesToProcess = new List<BaseNodeController>();
                if (NodeCardManager.Instance != null && NodeCardManager.Instance.HasSelection())
                {
                    var selected = NodeCardManager.Instance.GetSelectedNodes();
                    if (selected.Contains(_myController))
                        nodesToProcess.AddRange(selected);
                    else
                        nodesToProcess.Add(_myController);
                }
                else
                {
                    nodesToProcess.Add(_myController);
                }

                bool anySuccess = false;

                foreach (var node in nodesToProcess)
                {
                    if (node == potentialParent) continue;
                    if (NodeCardManager.Instance.IsDescendant(potentialParent, node)) continue;
                    if (node.parentNode != null && nodesToProcess.Contains(node.parentNode)) continue;

                    if (CommandManager.Instance != null)
                    {
                        var cmd = new ConnectionCommand(potentialParent, node, true);
                        CommandManager.Instance.ExecuteCommand(cmd);
                        anySuccess = true;
                    }
                    else
                    {
                        NodeLinkManager.Instance.CreateConnection(potentialParent, node);
                        anySuccess = true;
                    }
                }
                return anySuccess;
            }
        }
        if (potentialSibling != null && _myController.parentNode != null)
        {
            if (_activeProxies.Count > 1) return false;

            int currentIndex = _targetRect.GetSiblingIndex();
            int targetIndex = potentialSibling.transform.GetSiblingIndex();
            if (_detector.IsInsertAfter) targetIndex++;
            if (currentIndex < targetIndex && !_detector.IsInsertAfter) targetIndex--;
            _targetRect.SetSiblingIndex(targetIndex);
            if (NodeLinkManager.Instance != null) NodeLinkManager.Instance.ReorderChildren(_myController.parentNode);
            if (AutoLayoutSystem.Instance != null) AutoLayoutSystem.Instance.RefreshLayout(_myController.parentNode);
            return true;
        }
        if (_myController.parentNode != null)
        {
            if (AutoLayoutSystem.Instance != null) AutoLayoutSystem.Instance.RefreshLayout(_myController.parentNode);
            return true;
        }
        return false;
    }

    // ==========================================
    // 核心修复：强行无损恢复节点的初始状态和顺序
    // ==========================================
    private void ForceRestoreOriginalState()
    {
        // 1. 恢复被斩断的连线
        if (NodeLinkManager.Instance != null)
        {
            foreach (var pair in _detachedStates)
            {
                if (pair.Key != null && pair.Value.originalParent != null)
                {
                    NodeLinkManager.Instance.CreateConnection(pair.Value.originalParent, pair.Key);
                    // 恢复 Unity 物理层级
                    pair.Key.transform.SetSiblingIndex(pair.Value.originalSiblingIndex);
                    // 【核心修复】：必须重排数据列表，防止被 AutoLayout 扔到最下面！
                    NodeLinkManager.Instance.ReorderChildren(pair.Value.originalParent);
                }
            }
        }
        _detachedStates.Clear();

        // 2. 恢复自己原本的坐标和层级
        _targetRect.anchoredPosition = _originalPosition;
        targetRoot.SetSiblingIndex(_originalSiblingIndex);

        // 如果自己有父节点，再对父节点进行一次重排，确保万无一失
        if (_originalParent != null)
        {
            var pNode = _originalParent.GetComponent<BaseNodeController>();
            if (pNode != null && NodeLinkManager.Instance != null)
            {
                NodeLinkManager.Instance.ReorderChildren(pNode);
            }
        }

        // 3. 触发自动布局，立刻生效
        if (_myController.parentNode != null && AutoLayoutSystem.Instance != null)
        {
            AutoLayoutSystem.Instance.RefreshLayout(_myController.parentNode);
        }
        else if (AutoLayoutSystem.Instance != null)
        {
            AutoLayoutSystem.Instance.RefreshLayout(_myController);
        }
    }
}