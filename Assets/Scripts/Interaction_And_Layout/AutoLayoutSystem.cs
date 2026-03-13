using UnityEngine;
using UnityEngine.UI; // 必须引用，用于 LayoutRebuilder
using System.Collections;
using System.Collections.Generic;

public class AutoLayoutSystem : MonoBehaviour
{
    public static AutoLayoutSystem Instance;

    public float gapX = 150f; // 水平间距 (父右边缘 -> 子左边缘)
    public float gapY = 20f;  // 垂直间距 (兄弟之间)
    public float moveSpeed = 10f;

    // 缓存每个节点计算出的子树高度 (Key: Node, Value: Height)
    private Dictionary<BaseNodeController, float> _subtreeHeights = new Dictionary<BaseNodeController, float>();

    // 记录正在移动的协程，防止冲突
    private Dictionary<BaseNodeController, Coroutine> _activeMoves = new Dictionary<BaseNodeController, Coroutine>();

    void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 整理某个节点所在的整棵树
    /// </summary>
    public void RefreshLayout(BaseNodeController nodeInTree)
    {
        if (nodeInTree == null) return;

        // 1. 找到老祖宗 (从根开始排)
        BaseNodeController root = FindTreeRoot(nodeInTree);

        // 2. 开始排布
        ArrangeTree(root);
    }

    public void ArrangeAll()
    {
        if (NodeCardManager.Instance == null) return;
    }

    public void ArrangeTree(BaseNodeController treeRoot)
    {
        if (treeRoot == null) return;

        // --- 第一步：自底向上，计算高度 ---
        CalculateSubtreeHeight(treeRoot);

        // --- 准备工作：还原几何中心 ---
        // 我们要保持根节点视觉不动，所以要以它当前的几何中心为起点
        RectTransform rootRT = treeRoot.GetComponent<RectTransform>();
        Vector2 rootCurrentPos = rootRT.anchoredPosition;

        float rootWidth = rootRT.rect.width;
        float rootHeight = rootRT.rect.height;

        // 公式：几何中心 = 锚点坐标 - (Pivot偏移量)
        // 如果 Pivot 是 (0.5, 0.5)，偏移就是 0
        float startGeometricX = rootCurrentPos.x - (rootRT.pivot.x - 0.5f) * rootWidth;
        float startGeometricY = rootCurrentPos.y - (rootRT.pivot.y - 0.5f) * rootHeight;

        // --- 第二步：自顶向下，递归设置位置 ---
        SetNodePositionRecursive(treeRoot, startGeometricX, startGeometricY);
    }

    // Pass 1: 计算高度 (递归后序遍历)
    private float CalculateSubtreeHeight(BaseNodeController node)
    {
        // 1. 强制刷新 UI 尺寸 (确保文字变多后高度是最新的)
        RectTransform rt = node.GetComponent<RectTransform>();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        float myHeight = rt.rect.height;

        // 2. 如果是叶子节点 (没有孩子)
        if (node.childNodes.Count == 0)
        {
            _subtreeHeights[node] = myHeight;
            return myHeight;
        }

        // 3. 如果有孩子，计算“孩子块”的总高度
        float childrenBlockHeight = 0;
        foreach (var child in node.childNodes)
        {
            childrenBlockHeight += CalculateSubtreeHeight(child);
        }

        // 加上孩子之间的垂直间隙
        childrenBlockHeight += (node.childNodes.Count - 1) * gapY;

        // 4. 树的总高度 = Max(自身高度, 孩子块高度)
        // 通常孩子块会比父亲高，但也要防备父亲特别高的情况
        float finalHeight = Mathf.Max(myHeight, childrenBlockHeight);

        _subtreeHeights[node] = finalHeight;
        return finalHeight;
    }

    // Pass 2: 设置位置 (递归前序遍历)
    // geometricCenterX/Y: 这里的坐标是“纯几何中心”，不包含 Pivot 偏移
    private void SetNodePositionRecursive(BaseNodeController node, float geometricCenterX, float geometricCenterY)
    {
        RectTransform nodeRT = node.GetComponent<RectTransform>();
        float width = nodeRT.rect.width;
        float height = nodeRT.rect.height;

        // --- A. 执行移动 (需要把几何中心还原回 Pivot 坐标) ---
        float finalX = geometricCenterX + (nodeRT.pivot.x - 0.5f) * width;
        float finalY = geometricCenterY + (nodeRT.pivot.y - 0.5f) * height;

        MoveNodeTo(node, new Vector2(finalX, finalY));

        // --- B. 处理子节点 ---
        if (node.childNodes.Count == 0) return;

        float parentHalfWidth = width / 2f;

        // 1. 算出“孩子块”的纯高度 (不含父节点影响)
        float pureChildrenBlockHeight = 0;
        foreach (var child in node.childNodes)
        {
            if (_subtreeHeights.ContainsKey(child))
                pureChildrenBlockHeight += _subtreeHeights[child];
        }
        pureChildrenBlockHeight += (node.childNodes.Count - 1) * gapY;

        // 2. 计算起始 Y (从最上面开始排)
        // 顶部 Y = 父中心Y + (总高度 / 2)
        float currentY = geometricCenterY + (pureChildrenBlockHeight / 2f);

        // 3. 遍历孩子放置
        foreach (var child in node.childNodes)
        {
            RectTransform childRT = child.GetComponent<RectTransform>();
            float childHalfWidth = childRT.rect.width / 2f;
            float childSubtreeHeight = _subtreeHeights[child];

            // 算孩子的 X 几何中心
            // 父中心 + 父半宽 + 间距 + 子半宽
            float childGeometricX = geometricCenterX + parentHalfWidth + gapX + childHalfWidth;

            // 算孩子的 Y 几何中心
            // 当前游标 - (子树高 / 2) -> 这样子树的中心就对齐了游标位置
            float childGeometricY = currentY - (childSubtreeHeight / 2f);

            // 递归下去
            SetNodePositionRecursive(child, childGeometricX, childGeometricY);

            // 游标下移 (减去当前子树高 + 间隙)
            currentY -= (childSubtreeHeight + gapY);
        }
    }

    public BaseNodeController FindTreeRoot(BaseNodeController node)
    {
        if (node == null) return null;
        if (node.parentNode == null) return node;
        return FindTreeRoot(node.parentNode);
    }

    private void MoveNodeTo(BaseNodeController node, Vector2 targetPos)
    {
        if (node == null) return;

        // 如果已经在移动，先停掉旧的
        if (_activeMoves.ContainsKey(node))
        {
            if (_activeMoves[node] != null) StopCoroutine(_activeMoves[node]);
            _activeMoves.Remove(node);
        }

        // 启动新协程
        Coroutine co = StartCoroutine(SmoothMove(node.transform, targetPos, node));
        _activeMoves[node] = co;
    }

    private IEnumerator SmoothMove(Transform target, Vector2 targetPos, BaseNodeController key)
    {
        RectTransform rt = target.GetComponent<RectTransform>();
        float threshold = 0.5f;

        // 简单的阻尼移动
        while (Vector2.Distance(rt.anchoredPosition, targetPos) > threshold)
        {
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, targetPos, Time.deltaTime * moveSpeed);
            yield return null;
        }

        rt.anchoredPosition = targetPos; // 确保归位
        _activeMoves.Remove(key);
    }
}
