using UnityEngine;

public class CreateNodeCommand : ICommand
{
    private BaseNodeController _createdNode;
    private string _initialID;

    // 用于恢复 NodeCard 的父子关系
    private BaseNodeController _historyParent;

    // [新增] 明确区分是“首次创建”还是“撤销后重做”
    private bool _isFirstExecution = true;

    public CreateNodeCommand(BaseNodeController node)
    {
        _createdNode = node;
        _initialID = node.NodeID;
    }

    private string CurrentID
    {
        get
        {
            if (_createdNode != null) return _createdNode.NodeID;
            return _initialID;
        }
    }

    public void Execute()
    {
        if (_createdNode == null) return;

        // --- 1. 首次执行 ---
        if (_isFirstExecution)
        {
            _isFirstExecution = false;
            // 首次执行：只记埋点，不执行物理创建（因为实例化已经在 Manager 里做完了）
            LogCreationEvent();
            return;
        }

        // --- 2. Redo (重做/复活) ---
        _createdNode.gameObject.SetActive(true);
        string realID = _createdNode.NodeID;

        // A. 从回收站移回到默认容器 (画布)
        if (NodeCardManager.Instance != null && NodeCardManager.Instance.recycleBin != null)
        {
            if (_createdNode.transform.parent == NodeCardManager.Instance.recycleBin)
            {
                _createdNode.transform.SetParent(NodeCardManager.Instance.cardContainer);
            }
        }

        // B. 重新注册为节点
        if (NodeCardManager.Instance != null)
        {
            NodeCardManager.Instance.RegisterNodeCard(realID, _createdNode);
        }

        // C. 恢复连线与排版
        if (_historyParent != null && NodeLinkManager.Instance != null)
        {
            if (_historyParent.gameObject.activeInHierarchy)
            {
                NodeLinkManager.Instance.CreateConnection(_historyParent, _createdNode);
                if (AutoLayoutSystem.Instance != null)
                {
                    AutoLayoutSystem.Instance.RefreshLayout(_historyParent);
                }
            }
        }

        // 重做时也算作一次生成行为记录
        LogCreationEvent();
    }

    public void Undo()
    {
        if (_createdNode == null) return;

        string realID = _createdNode.NodeID;

        // 1. 记录父节点 (为了下次 Redo 连线和排版)
        _historyParent = _createdNode.parentNode;

        // 2. 数据层断开连接
        if (NodeLinkManager.Instance != null)
        {
            NodeLinkManager.Instance.DeleteNodeCleanup(realID);
        }

        // 3. 移入物理回收站
        if (NodeCardManager.Instance != null && NodeCardManager.Instance.recycleBin != null)
        {
            _createdNode.transform.SetParent(NodeCardManager.Instance.recycleBin);
        }

        // 4. 隐藏节点
        _createdNode.gameObject.SetActive(false);

        // 5. 注销全局 ID
        if (NodeCardManager.Instance != null)
        {
            NodeCardManager.Instance.UnregisterNodeCard(realID);
        }

        // 6. 刷新父节点布局 (如果父节点还在画布上)
        if (AutoLayoutSystem.Instance != null && _historyParent != null)
        {
            if (_historyParent.gameObject.activeInHierarchy)
            {
                AutoLayoutSystem.Instance.RefreshLayout(_historyParent);
            }
        }
    }

    public string GetLogInfo()
    {
        return $"Create: {CurrentID}";
    }

    // [新增] 将埋点逻辑抽离为独立方法，确保首次创建和 Redo 都能准确记录
    private void LogCreationEvent()
    {
        if (UserBehaviorSystem.Instance != null && _createdNode.gameObject.activeSelf)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.Canvas_CreateNode,
                targetID: CurrentID,
                info: "NodeCard",
                value: 1
            );
        }
    }
}