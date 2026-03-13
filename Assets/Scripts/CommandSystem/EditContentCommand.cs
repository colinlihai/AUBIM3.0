using UnityEngine;

public class EditContentCommand : ICommand
{
    private BaseNodeController _node;
    private string _oldText;
    private string _newText;
    private string _nodeID;

    // 构造函数：记录修改前后的文本
    public EditContentCommand(BaseNodeController node, string oldText, string newText)
    {
        _node = node;
        _oldText = oldText;
        _newText = newText;
        _nodeID = node.NodeID;
    }

    public void Execute()
    {
        ApplyText(_newText);

        TriggerAIChecks();

        // [埋点] 正文编辑完成
        // 计算字数变化量：正数=产出，负数=删减
        int delta = _newText.Length - _oldText.Length;

        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.Edit_Node_Body_End,
                targetID: _nodeID,
                info: $"Len:{_oldText.Length}->{_newText.Length}",
                value: delta // <--- 关键特征：产出量
            );
        }

        // [核心探针] 发现用户正在修改 AI 给的文案
        if (_node.isCognitiveNode && UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.AI_Intervention_Internalized,
                targetID: _nodeID,
                info: $"Internalized:{_node.cognitiveType}",
                value: Mathf.Abs(delta) // 记录修改的力度
            );
            Debug.Log($"<color=yellow>[研究追踪]</color> 用户内化并修改了 AI 的 {_node.cognitiveType} 节点。");
        }
    }

    public void Undo()
    {
        ApplyText(_oldText);
    }

    private void ApplyText(string text)
    {
        if (_node != null && _node.Data != null)
        {
            // 1. 更新数据模型
            _node.Data.Content = text;

            // 2. 刷新 UI 显示 (这将触发 InputField 更新)
            _node.RefreshUI();

            // 3. 强制触发布局更新 (因为文字变长/变短会影响高度)
            // [3.0 优化] 移除了 CardType.NodeCard 的类型限制，万物皆 Node，一律排版
            if (AutoLayoutSystem.Instance != null)
            {
                AutoLayoutSystem.Instance.RefreshLayout(_node);
            }
        }
    }

    // [3.0 优化] 彻底简化的 AI 触发链路
    private void TriggerAIChecks()
    {
        if (_node == null) return;

        // 现在节点自己管自己，如果它是 NodeController，就直接触发智能拟题
        if (_node is NodeController nc)
        {
            nc.TryRefreshTitleByAI();
        }
    }

    public string GetLogInfo()
    {
        // 日志里只记录前10个字符，避免 JSON 文件太大
        string shortOld = _oldText.Length > 10 ? _oldText.Substring(0, 10) + "..." : _oldText;
        string shortNew = _newText.Length > 10 ? _newText.Substring(0, 10) + "..." : _newText;
        return $"Edit: {_nodeID} (Len: {_oldText.Length}->{_newText.Length})";
    }
}