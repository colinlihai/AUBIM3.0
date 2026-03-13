using UnityEngine;
using UnityEngine.UI;

public class ReparentUICommand : ICommand
{
    private BaseNodeController _targetNode;
    private Transform _oldParent;
    private Transform _newParent;
    private int _oldSiblingIndex;
    private int _newSiblingIndex;
    private string _nodeID;

    public ReparentUICommand(BaseNodeController node, Transform oldParent, Transform newParent, int oldIndex, int newIndex)
    {
        _targetNode = node;
        _oldParent = oldParent;
        _newParent = newParent;
        _oldSiblingIndex = oldIndex;
        _newSiblingIndex = newIndex;
        _nodeID = node.NodeID;
    }

    public void Execute()
    {
        // Redo: 去新家
        MoveTo(_newParent, _newSiblingIndex);
    }

    public void Undo()
    {
        // Undo: 回老家
        MoveTo(_oldParent, _oldSiblingIndex);
    }

    private void MoveTo(Transform parent, int index)
    {
        if (_targetNode != null && parent != null)
        {
            // 1. 物理层级移动
            _targetNode.transform.SetParent(parent);
            _targetNode.transform.SetSiblingIndex(index);

            // 2. 强制刷新布局 (让挂载的 AutoWidth / AutoHeight 组件重新计算尺寸)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent as RectTransform);

            // [3.0 优化] 移除了原有的 UpdateGroupSummary 逻辑。
            // 因为现在的架构中不再有 Group 实体，层级移动后的逻辑结算将由 AutoLayoutSystem 和 NodeLinkManager 自动闭环。
        }
    }

    public string GetLogInfo()
    {
        return $"Reparent: {_nodeID}";
    }
}