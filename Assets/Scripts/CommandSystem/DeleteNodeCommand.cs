using UnityEngine;
using System.Collections.Generic;

public class DeleteNodeCommand : ICommand
{
    private BaseNodeController _targetNode;   // 选中的节点
    private string _nodeID;

    // --- 目标节点的快照 ---
    private Transform _targetOriginalParent;
    private int _targetOriginalIndex;

    // --- 关系快照 (数据层) ---
    private BaseNodeController _parentNode;
    private List<BaseNodeController> _childNodesSnapshot;

    public DeleteNodeCommand(BaseNodeController node)
    {
        _targetNode = node;
        _nodeID = node.NodeID;

        // 1. 记录目标节点物理状态
        _targetOriginalParent = node.transform.parent;
        _targetOriginalIndex = node.transform.GetSiblingIndex();

        // 2. 记录逻辑关系 (用于恢复连线)
        _parentNode = node.parentNode;
        if (node.childNodes != null)
        {
            _childNodesSnapshot = new List<BaseNodeController>(node.childNodes);
        }
    }

    public void Execute()
    {
        _targetNode.SetSelected(false);

        // 1. 数据层清理 (断开连线并处理子节点)
        if (NodeLinkManager.Instance != null)
        {
            NodeLinkManager.Instance.DeleteNodeCleanup(_nodeID);
        }

        // 2. 软删除本体
        SoftDelete(_targetNode);

        // 3. 刷新布局
        if (AutoLayoutSystem.Instance != null && _parentNode != null)
        {
            // 确保父节点还活着
            if (_parentNode.gameObject.activeInHierarchy)
            {
                AutoLayoutSystem.Instance.RefreshLayout(_parentNode);
            }
        }

        // [埋点] 删除对象
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.Object_Delete,
                targetID: _nodeID,
                info: "DeleteNode",
                value: 1
            );
        }

        // [核心探针] 检测被删的这家伙是不是 AI 认知节点
        if (_targetNode.isCognitiveNode && UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.AI_Intervention_Rejected,
                targetID: _nodeID,
                info: $"Rejected:{_targetNode.cognitiveType}"
            );
            Debug.Log($"<color=red>[研究追踪]</color> 用户无情拒绝了 AI 的 {_targetNode.cognitiveType} 建议！");
        }
    }

    public void Undo()
    {
        // 1. 复活本体
        ReviveNode(_targetNode, _targetOriginalParent, _targetOriginalIndex);

        // 2. 重新注册
        if (NodeCardManager.Instance != null)
        {
            NodeCardManager.Instance.RegisterNodeCard(_nodeID, _targetNode);
        }

        // 3. 恢复连线 (因为之前清理了)
        if (NodeLinkManager.Instance != null)
        {
            // 恢复与父节点的连线
            if (_parentNode != null && _parentNode.gameObject.activeInHierarchy)
                NodeLinkManager.Instance.CreateConnection(_parentNode, _targetNode);

            // 恢复与子节点的连线
            if (_childNodesSnapshot != null)
            {
                foreach (var child in _childNodesSnapshot)
                {
                    if (child != null && child.gameObject.activeInHierarchy)
                        NodeLinkManager.Instance.CreateConnection(_targetNode, child);
                }
            }

            // 重新排布父节点的子节点顺序
            if (_parentNode != null)
            {
                NodeLinkManager.Instance.ReorderChildren(_parentNode);
            }
        }

        // 4. 刷新布局
        if (AutoLayoutSystem.Instance != null)
        {
            if (_parentNode != null) AutoLayoutSystem.Instance.RefreshLayout(_parentNode);
            else AutoLayoutSystem.Instance.RefreshLayout(_targetNode);
        }
    }

    // --- 辅助方法 ---

    private void SoftDelete(BaseNodeController node)
    {
        if (node == null) return;

        // 移入回收站
        if (NodeCardManager.Instance != null && NodeCardManager.Instance.recycleBin != null)
        {
            node.transform.SetParent(NodeCardManager.Instance.recycleBin);
        }

        node.gameObject.SetActive(false);

        // 注销
        if (NodeCardManager.Instance != null)
        {
            NodeCardManager.Instance.UnregisterNodeCard(node.NodeID);
        }
    }

    private void ReviveNode(BaseNodeController node, Transform originalParent, int originalIndex)
    {
        if (node == null) return;

        node.gameObject.SetActive(true);
        node.SetSelected(false);

        // 尝试回原来的家
        if (originalParent != null)
        {
            node.transform.SetParent(originalParent);
        }
        else
        {
            // 兜底：回根目录 (Canvas)
            if (NodeCardManager.Instance != null)
            {
                node.transform.SetParent(NodeCardManager.Instance.cardContainer);
            }
        }

        node.transform.SetSiblingIndex(originalIndex);

        // 强制刷新 UI 布局，防止位置错乱
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(node.GetComponent<RectTransform>());
    }

    public string GetLogInfo()
    {
        return $"Delete: {_nodeID}";
    }
}