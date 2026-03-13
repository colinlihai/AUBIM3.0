using UnityEngine;
using System.Collections.Generic;

public class NodeLinkManager : MonoBehaviour
{
    public static NodeLinkManager Instance;

    public Transform lineLayer;

    [System.Serializable]
    public class NodeConnection
    {
        public string sourceID; // 父 ID
        public string targetID; // 子 ID
        public BaseNodeController sourceNode;
        public BaseNodeController targetNode;
        public ConnectionLine visualLine; // 引用我们在第一阶段写的 ConnectionLine 脚本
    }

    private List<NodeConnection> _allConnections = new List<NodeConnection>();

    void Awake()
    {
        Instance = this;
    }

    void LateUpdate()
    {
        // 每帧刷新所有线条的视觉位置 (正交线跟随)
        foreach (var conn in _allConnections)
        {
            if (conn.visualLine != null)
            {
                conn.visualLine.UpdateGeometry();
            }
        }
    }

    public void ClearAll()
    {
        // 1. 销毁所有视觉线条
        foreach (var conn in _allConnections)
        {
            if (conn.visualLine != null)
                Destroy(conn.visualLine.gameObject);
        }

        // 2. 清空数据列表
        _allConnections.Clear();

        // 3. (双重保险) 清空 LineLayer 下的所有残留物体
        if (lineLayer != null)
        {
            foreach (Transform child in lineLayer)
            {
                Destroy(child.gameObject);
            }
        }
    }

    public void CreateConnection(BaseNodeController parent, BaseNodeController child)
    {
        if (parent == null || child == null || parent == child) return;

        // 1. 环路检测 (防止死循环：不能认自己的后代做爸爸)
        if (CheckCircularReference(parent, child))
        {
            Debug.LogWarning($"[LinkManager] 环路警告: {child.NodeID} 已经是 {parent.NodeID} 的祖先，无法建立连接。");
            return;
        }

        // 2. 改嫁逻辑：如果孩子已经有爸爸，先断开旧关系
        // (树形结构规定：一个节点只能有一个父节点)
        string existingParentID = GetParentID(child.NodeID);
        if (!string.IsNullOrEmpty(existingParentID))
        {
            // 如果已经是现在的爸爸，不用动
            if (existingParentID == parent.NodeID) return;

            // 否则，先与旧爸爸断绝关系
            BreakConnection(child);
        }

        // 3. 数据层更新 (Sync Data)
        // 这一步是为了让 AutoLayout 能访问到 parentNode 和 childNodes
        child.parentNode = parent;
        if (!parent.childNodes.Contains(child))
        {
            parent.childNodes.Add(child);
        }

        // 4. 视觉层生成 (Create Visual)
        CreateVisualLine(parent, child);

        // [新增] 5. 触发自动布局
        if (AutoLayoutSystem.Instance != null)
        {
            // 只要这一家子变动了，就让老祖宗重新排队
            AutoLayoutSystem.Instance.RefreshLayout(parent);
        }
    }

    public void BreakConnection(BaseNodeController child)
    {
        if (child == null) return;

        // 在列表里找到“目标是这个孩子”的连接
        NodeConnection conn = _allConnections.Find(x => x.targetID == child.NodeID);
        if (conn == null) return; // 本来就是自由节点

        // 1. 数据层清理
        if (conn.sourceNode != null)
        {
            conn.sourceNode.childNodes.Remove(child);
        }
        child.parentNode = null;

        BaseNodeController oldParent = conn.sourceNode;

        // 2. 视觉层清理
        if (conn.visualLine != null)
        {
            Destroy(conn.visualLine.gameObject);
        }

        // 3. 从总表中移除
        _allConnections.Remove(conn);

        if (AutoLayoutSystem.Instance != null && oldParent != null)
        {
            AutoLayoutSystem.Instance.RefreshLayout(oldParent);
        }
    }

    private void CreateVisualLine(BaseNodeController parent, BaseNodeController child)
    {
        // 生成空物体
        GameObject lineObj = new GameObject($"Link_{parent.NodeID}_{child.NodeID}");

        // 挂载我们之前写的 ConnectionLine 脚本
        ConnectionLine lineScript = lineObj.AddComponent<ConnectionLine>();

        // 初始化配置
        lineScript.Initialize(lineLayer); // 确保 lineLayer 已赋值且在 CardContainer 下层

        // 自动识别 Visual (核心修复：连接到有颜色的 CoreBody)
        if (parent.visual != null) lineScript.startNode = parent.visual.GetComponent<RectTransform>();
        else lineScript.startNode = parent.GetComponent<RectTransform>();

        if (child.visual != null) lineScript.endNode = child.visual.GetComponent<RectTransform>();
        else lineScript.endNode = child.GetComponent<RectTransform>();

        // 存入列表
        NodeConnection newConn = new NodeConnection();
        newConn.sourceID = parent.NodeID;
        newConn.targetID = child.NodeID;
        newConn.sourceNode = parent;
        newConn.targetNode = child;
        newConn.visualLine = lineScript;

        _allConnections.Add(newConn);

        // 立即刷新一次位置
        lineScript.UpdateGeometry();
    }

    private bool CheckCircularReference(BaseNodeController potentialParent, BaseNodeController child)
    {
        BaseNodeController current = potentialParent;
        int safety = 0;

        while (current != null && safety < 1000)
        {
            if (current == child) return true; // 发现 parent 的祖先竟然是 child！
            current = current.parentNode;      // 继续往上找
            safety++;
        }
        return false;
    }

    public string GetParentID(string childID)
    {
        var conn = _allConnections.Find(x => x.targetID == childID);
        return conn != null ? conn.sourceID : null;
    }

    public void DeleteNodeCleanup(string nodeID)
    {
        // 当删除一个节点时，清理所有相关的连接
        // 1. 我作为子节点 (断开与父的连接) -> BreakConnection 会处理
        // 2. 我作为父节点 (销毁连向子节点的线，子节点变孤儿)

        // 找出所有连向孩子的线
        List<NodeConnection> childLinks = _allConnections.FindAll(x => x.sourceID == nodeID);
        foreach (var link in childLinks)
        {
            // 只是销毁线条和关系，孩子变自由，不删孩子
            if (link.targetNode != null) link.targetNode.parentNode = null;
            if (link.visualLine != null) Destroy(link.visualLine.gameObject);
        }
        _allConnections.RemoveAll(x => x.sourceID == nodeID);

        // 找出连向父亲的线
        NodeConnection parentLink = _allConnections.Find(x => x.targetID == nodeID);
        if (parentLink != null)
        {
            if (parentLink.sourceNode != null) parentLink.sourceNode.childNodes.Remove(parentLink.targetNode);
            if (parentLink.visualLine != null) Destroy(parentLink.visualLine.gameObject);
            _allConnections.Remove(parentLink);
        }
    }

    public void ReorderChildren(BaseNodeController parent)
    {
        if (parent == null) return;

        // 根据 transform.GetSiblingIndex() 进行排序
        parent.childNodes.Sort((a, b) =>
        {
            return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        });
    }
}
