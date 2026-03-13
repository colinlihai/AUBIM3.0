using UnityEngine;
using UnityEngine.UI;

public class ReorderSiblingCommand : ICommand
{
    private BaseNodeController _targetNode;
    private int _oldIndex;
    private int _newIndex;
    private string _nodeID;

    public ReorderSiblingCommand(BaseNodeController node, int oldIndex, int newIndex)
    {
        _targetNode = node;
        _oldIndex = oldIndex;
        _newIndex = newIndex;
        _nodeID = node.NodeID;
    }

    public void Execute()
    {
        ApplyIndex(_newIndex);

        // [埋点] 画布节点层级排序
        // 这是用户在组织逻辑流的高价值行为
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
               BehaviorEventType.Canvas_ReorderNode, // [修改] 变更为画布重排
               targetID: _nodeID,
               info: $"Idx:{_oldIndex}->{_newIndex}",
               value: Mathf.Abs(_newIndex - _oldIndex) // 变动幅度
           );
        }
    }

    public void Undo()
    {
        ApplyIndex(_oldIndex);
    }

    private void ApplyIndex(int index)
    {
        if (_targetNode != null)
        {
            _targetNode.transform.SetSiblingIndex(index);

            // 如果有父节点，通知父节点刷新连线顺序 (NodeLinkManager)
            if (_targetNode.parentNode != null && NodeLinkManager.Instance != null)
            {
                NodeLinkManager.Instance.ReorderChildren(_targetNode.parentNode);
            }

            // 刷新自动布局 (确保位置正确)
            if (AutoLayoutSystem.Instance != null && _targetNode.parentNode != null)
            {
                AutoLayoutSystem.Instance.RefreshLayout(_targetNode.parentNode);
            }
        }
    }

    public string GetLogInfo()
    {
        return $"Reorder: {_nodeID} ({_oldIndex}->{_newIndex})";
    }
}