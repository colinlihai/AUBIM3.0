using UnityEngine;

public class MoveNodeCommand : ICommand
{
    private RectTransform _targetRect;
    private Vector2 _oldPos;
    private Vector2 _newPos;
    private string _nodeID;

    private bool _isDetachMove;
    private BaseNodeController _node;

    // 构造函数：在操作发生时，把“案发现场”的数据存下来
    public MoveNodeCommand(BaseNodeController node, Vector2 oldPos, Vector2 newPos, bool isDetachMove)
    {
        _targetRect = node.GetComponent<RectTransform>();
        _oldPos = oldPos;
        _newPos = newPos;
        _nodeID = node.NodeID;
        _isDetachMove = isDetachMove;
    }

    public void Execute()
    {
        // 【新增】：只要用户拖拽移动了节点，立即实体化
        if (_node != null) _node.SolidifyCognitiveNode();

        // 重做 (Redo) 时：去新位置
        if (_targetRect != null)
        {
            _targetRect.anchoredPosition = _newPos;
        }

        // [埋点] 移动节点
        // 计算移动距离作为 Value，距离越大说明布局调整意图越强
        float dist = Vector2.Distance(_oldPos, _newPos);

        // 过滤微小抖动
        if (dist > 10f && UserBehaviorSystem.Instance != null)
        {
            if (_isDetachMove)
            {
                Debug.Log($"<color=yellow>[MoveCommand 诊断]</color> 节点 {_nodeID} 是分离移动，已由 Link_Break 记录，忽略本次 Move 埋点。");
                return;
            }

            BehaviorEventType eventType = _isDetachMove ?
                BehaviorEventType.Canvas_Node_DetachMove :
                BehaviorEventType.Canvas_Node_Move;

            // Info 里也可以打上更明显的标签以防万一
            string infoLabel = _isDetachMove ? "Context: Detach" : "Context: FreeMove";

            // 专属诊断日志
            Debug.Log($"<color=yellow>[MoveCommand 诊断]</color> 节点 {_nodeID} 移动。是否分离: {_isDetachMove} -> 即将记录为: {eventType}");

            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.Canvas_Node_Move,
                targetID: _nodeID,
                info: infoLabel,
                value: dist // 特征：移动距离
            );
        }
    }

    public void Undo()
    {
        // 撤销 (Undo) 时：回旧位置
        if (_targetRect != null)
        {
            _targetRect.anchoredPosition = _oldPos;
        }
    }

    public string GetLogInfo()
    {
        string prefix = _isDetachMove ? "DetachMove" : "FreeMove";
        return $"{prefix}: {_nodeID}";
    }
}