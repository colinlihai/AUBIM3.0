using UnityEngine;

public class ConnectionCommand : ICommand
{
    private BaseNodeController _parent;
    private BaseNodeController _child;
    private int _originalSiblingIndex; // 关键：用于撤销“断开”时恢复排位
    private bool _isLinkOperation;     // true = 建立连接; false = 断开连接

    private string _childID;
    private string _parentID;

    // 构造函数
    // isLink = true:  把 child 连给 parent (执行 Link)
    // isLink = false: 把 child 从 parent 身上断开 (执行 Unlink)
    public ConnectionCommand(BaseNodeController parent, BaseNodeController child, bool isLink, int originalIndex = -1)
    {
        _parent = parent;
        _child = child;
        _isLinkOperation = isLink;
        _originalSiblingIndex = originalIndex;

        _childID = child.NodeID;
        _parentID = parent != null ? parent.NodeID : "null";
    }

    public void Execute()
    {
        if (_isLinkOperation)
        {
            // 执行连线
            DoLink();
        }
        else
        {
            // 执行断开
            DoUnlink();
        }
    }

    public void Undo()
    {
        if (_isLinkOperation)
        {
            // 撤销连线 -> 也就是断开
            DoUnlink();
        }
        else
        {
            // 撤销断开 -> 也就是重新连上，并恢复排位
            DoLink();

            // 恢复排位 (SiblingIndex)
            if (_originalSiblingIndex >= 0 && _child != null)
            {
                _child.transform.SetSiblingIndex(_originalSiblingIndex);
                // 通知 LinkManager 刷新连线顺序
                if (NodeLinkManager.Instance != null && _parent != null)
                {
                    NodeLinkManager.Instance.ReorderChildren(_parent);
                }
                // 刷新布局
                if (AutoLayoutSystem.Instance != null && _parent != null)
                {
                    AutoLayoutSystem.Instance.RefreshLayout(_parent);
                }
            }
        }
    }

    private void DoLink()
    {
        if (NodeLinkManager.Instance != null && _parent != null && _child != null)
        {
            NodeLinkManager.Instance.CreateConnection(_parent, _child);

            // [埋点] 建立连接 (PDA High)
            if (UserBehaviorSystem.Instance != null)
                UserBehaviorSystem.Instance.LogEvent(
                    BehaviorEventType.Canvas_LinkNodes,
                    targetID: _parentID,
                    info: $"To:{_childID}",
                    value: 1
                );

            // [核心探针] 如果用户把线连在一个 AI 节点下面（AI 节点作为父亲），说明被启发了！
            if (_parent.isCognitiveNode && UserBehaviorSystem.Instance != null)
            {
                UserBehaviorSystem.Instance.LogEvent(
                    BehaviorEventType.AI_Intervention_Extended,
                    targetID: _parentID,
                    info: $"Extended:{_parent.cognitiveType} -> {_childID}",
                    value: 1
                );
                Debug.Log($"<color=green>[研究追踪]</color> 漂亮！AI 的 {_parent.cognitiveType} 成功启发用户创建了新节点！");
            }
        }
    }

    private void DoUnlink()
    {
        if (NodeLinkManager.Instance != null && _child != null)
        {
            NodeLinkManager.Instance.BreakConnection(_child);

            // [埋点] 断开连接 (BR High)
            if (UserBehaviorSystem.Instance != null)
                UserBehaviorSystem.Instance.LogEvent(
                    BehaviorEventType.Link_Break,
                    targetID: _childID,
                    value: 1
                );
        }
    }

    public string GetLogInfo()
    {
        string action = _isLinkOperation ? "Link" : "Unlink";
        return $"{action}: {_childID} <-> {_parentID}";
    }
}