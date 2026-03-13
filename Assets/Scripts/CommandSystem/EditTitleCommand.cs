using UnityEngine;

public class EditTitleCommand : ICommand
{
    private BaseNodeController _node;
    private string _oldTitle;
    private string _newTitle;
    private string _nodeID;

    public EditTitleCommand(BaseNodeController node, string oldTitle, string newTitle)
    {
        _node = node;
        _oldTitle = string.IsNullOrEmpty(oldTitle) ? "" : oldTitle;
        _newTitle = string.IsNullOrEmpty(newTitle) ? "" : newTitle;
        _nodeID = node.NodeID;
    }

    public void Execute()
    {
        ApplyTitle(_newTitle);

        // [埋点] 标题编辑完成
        int delta = _newTitle.Length - _oldTitle.Length;

        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.Edit_Node_Title_End,
                targetID: _nodeID,
                info: $"TitleChange",
                value: delta
            );
        }
    }

    public void Undo()
    {
        ApplyTitle(_oldTitle);
    }

    private void ApplyTitle(string title)
    {
        if (_node != null && _node.Data != null)
        {
            // 1. 更新数据
            _node.Data.Title = title;

            // 2. 刷新 UI
            // [3.0 优化] 彻底移除了 GroupController 的判断，统一由 NodeController 处理
            if (_node is NodeController nc && nc.titleInput != null)
            {
                nc.titleInput.text = title;
            }

            // 标题变化通常不影响 AutoLayout (除非标题换行)，这里暂不强制刷新布局
        }
    }

    public string GetLogInfo()
    {
        return $"EditTitle: {_nodeID} (Len: {_oldTitle.Length}->{_newTitle.Length})";
    }
}