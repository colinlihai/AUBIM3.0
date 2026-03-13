using UnityEngine;
using UnityEngine.UI;

public class ResizeNodeCommand : ICommand
{
    private LayoutElement _targetLayout;
    private float _oldWidth;
    private float _newWidth;
    private string _nodeID;
    private BaseNodeController _controller;
    private bool _wasAutoMode;
    public ResizeNodeCommand(BaseNodeController node, LayoutElement layout, float oldWidth, float newWidth, bool wasAuto)
    {
        _controller = node;
        _targetLayout = layout;
        _oldWidth = oldWidth;
        _newWidth = newWidth;
        _nodeID = node.NodeID;
        _wasAutoMode = wasAuto;
    }

    public void Execute()
    {
        SetState(_newWidth,false);
    }

    public void Undo()
    {
        SetState(_oldWidth, _wasAutoMode);
    }

    private void SetState(float width, bool isAuto)
    {
        if (_targetLayout != null)
        {
            _targetLayout.preferredWidth = width;

            // 同步设置 AutoWidthByContent 的开关
            var autoComp = _targetLayout.GetComponent<AutoWidthByContent>();
            if (autoComp != null)
            {
                autoComp.isAutoWidth = isAuto;

                // 如果切回自动模式，可能需要立刻刷新一下宽度以匹配当前文字
                if (isAuto && _controller != null && _controller.contentInput != null)
                {
                    autoComp.OnTextChanged(_controller.contentInput.text);
                }
            }

            // 刷新布局
            if (_controller != null && AutoLayoutSystem.Instance != null)
            {
                AutoLayoutSystem.Instance.RefreshLayout(_controller);
            }
        }
    }

    public string GetLogInfo()
    {
        return $"Resize: {_nodeID} ({_oldWidth:F0}->{_newWidth:F0})";
    }
}