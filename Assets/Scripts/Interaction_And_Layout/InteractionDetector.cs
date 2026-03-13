using UnityEngine;
using UnityEngine.EventSystems;

public class InteractionDetector : MonoBehaviour
{
    [Header("Interaction Settings")]
    [Tooltip("吸附检测：是否进入目标Rect的范围")]
    public bool useRectOverlapForAdsorb = true;

    [Tooltip("分离检测：相对于【起始位置】拉开多远算断开 (X轴, Y轴)")]
    public Vector2 detachThreshold = new Vector2(250f, 200f);

    [Tooltip("排序检测：只有水平距离小于此值时才触发排序")]
    public float reorderHorizontalThreshold = 100f;

    [Tooltip("排序保护通道：如果 X 轴偏移小于此值，视为意图在排序，忽略 Y 轴的分离阈值")]
    public float reorderSafeZoneWidth = 120f;

    // --- 公开的探测结果 (供 Router 读取) ---
    public BaseNodeController PotentialParent { get; private set; }
    public BaseNodeController PotentialSibling { get; private set; }
    public bool IsInsertAfter { get; private set; }

    public bool Detect(PointerEventData eventData, BaseNodeController myController, Vector3 currentDragWorldPos, Vector2 dragDelta)
    {
        // 1. 重置每帧状态
        PotentialParent = null;
        PotentialSibling = null;

        if (NodeCardManager.Instance == null) return false;
        var allNodes = NodeCardManager.Instance.GetAllNodes();

        BaseNodeController bestSibling = null;
        float minDistance = float.MaxValue;
        bool bestIsInsertAfter = false;
        RectTransform bestTargetRect = null;

        foreach (var node in allNodes)
        {
            if (node == myController) continue;
            if (IsDescendant(node, myController)) continue;

            RectTransform targetVisualRect = node.visual != null ?
                node.visual.GetComponent<RectTransform>() :
                node.GetComponent<RectTransform>();

            // 【关键新增】计算拖拽物体中心点相对于目标节点的局部坐标
            // 这将用于判断它到底在目标节点的上半部分、中间还是下半部分
            Vector3 localCenterPos = targetVisualRect.InverseTransformPoint(currentDragWorldPos);

            // ==============================================================================
            // A & B 融合：接触检测与三等分判定 (Adsorption & Reorder)
            // ==============================================================================
            // 当鼠标确实悬停在目标卡片上时触发：
            if (RectTransformUtility.RectangleContainsScreenPoint(targetVisualRect, eventData.position, eventData.pressEventCamera))
            {
                // 获取目标节点的高度，并计算三分之一的上下边界
                float height = targetVisualRect.rect.height;
                float topBoundary = targetVisualRect.rect.yMax - (height / 3f);
                float bottomBoundary = targetVisualRect.rect.yMin + (height / 3f);

                // 判断拖拽中心点落在了哪个区域
                if (localCenterPos.y > topBoundary && myController.parentNode != null && node.parentNode == myController.parentNode)
                {
                    // 上 1/3 区域，且有共同父节点 (是同级) -> 意图：排序在其上
                    PotentialSibling = node;
                    IsInsertAfter = false;
                    ShowInsertFeedback(targetVisualRect, IsInsertAfter);
                    return false;
                }
                else if (localCenterPos.y < bottomBoundary && myController.parentNode != null && node.parentNode == myController.parentNode)
                {
                    // 下 1/3 区域，且有共同父节点 (是同级) -> 意图：排序在其下
                    PotentialSibling = node;
                    IsInsertAfter = true;
                    ShowInsertFeedback(targetVisualRect, IsInsertAfter);
                    return false;
                }
                else
                {
                    // 中间 1/3 区域 (或者即使在上下区域，但不属于同一个父节点) -> 意图：吸附成子节点
                    PotentialParent = node;
                    if (DropFeedbackSystem.Instance) DropFeedbackSystem.Instance.HideAll();
                    return false; // 触发吸附，直接结束
                }
            }

            // ==============================================================================
            // B2. 原有的间隙排序检测 (兜底逻辑)
            // ==============================================================================
            // 如果鼠标不在任何卡片上，但是拖拽中心点靠近卡片间的缝隙，仍然允许排序
            if (myController.parentNode != null && node.parentNode == myController.parentNode)
            {
                float dynamicVerticalThreshold = (targetVisualRect.rect.height / 2) + 150f;
                bool verticalClose = Mathf.Abs(localCenterPos.y) < dynamicVerticalThreshold;
                bool horizontalClose = Mathf.Abs(localCenterPos.x) < reorderHorizontalThreshold;

                if (verticalClose && horizontalClose)
                {
                    float dist = Vector3.Distance(currentDragWorldPos, targetVisualRect.position);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestSibling = node;
                        bestTargetRect = targetVisualRect;
                        bestIsInsertAfter = localCenterPos.y < 0;
                    }
                }
            }
        }

        // 处理间隙间的排序结果
        if (bestSibling != null)
        {
            PotentialSibling = bestSibling;
            IsInsertAfter = bestIsInsertAfter;
            ShowInsertFeedback(bestTargetRect, IsInsertAfter);
            return false;
        }

        // ==============================================================================
        // C. 分离检测 (Detach) - 最低优先级
        // ==============================================================================
        if (myController.parentNode != null)
        {
            bool shouldDetach = Mathf.Abs(dragDelta.x) > detachThreshold.x;
            if (shouldDetach)
            {
                if (DropFeedbackSystem.Instance) DropFeedbackSystem.Instance.HideAll();
                return true;
            }
        }

        // 什么都没触发，隐藏反馈
        if (DropFeedbackSystem.Instance) DropFeedbackSystem.Instance.HideAll();
        return false;
    }

    // [新增辅助方法] 将重复的显示反馈线逻辑抽离出来，保持代码干净
    private void ShowInsertFeedback(RectTransform targetRect, bool isInsertAfter)
    {
        float offsetY = isInsertAfter ? -targetRect.rect.height / 2 : targetRect.rect.height / 2;
        Vector3 linePos = targetRect.position + targetRect.up * offsetY;
        if (DropFeedbackSystem.Instance) DropFeedbackSystem.Instance.ShowInsertion(linePos, targetRect.rect.width);
    }

    private bool IsDescendant(BaseNodeController nodeA, BaseNodeController nodeB)
    {
        var current = nodeA.parentNode;
        while (current != null) { if (current == nodeB) return true; current = current.parentNode; }
        return false;
    }
}