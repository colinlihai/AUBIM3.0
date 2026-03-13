using System;
using System.Collections.Generic;

[Serializable]
public class NodeData
{
    public string ID = Guid.NewGuid().ToString();
    public CardType Type;
    private string _content;
    public string Content
    {
        get => _content;
        set
        {
            if (_content != value)
            {
                _content = value;
                // 数据改变时，发出通知
                OnContentChanged?.Invoke(_content);
            }
        }
    }
    public string Title;
    public List<string> ChildrenIDs = new List<string>();
    // 定义一个事件
    public Action<string> OnContentChanged;
    public NodeData(CardType type)
    {
        this.Type = type;
    }
}