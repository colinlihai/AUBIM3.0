using UnityEngine;
using TMPro;

public class EditArticleCommand : ICommand
{
    private TMP_InputField _inputField;
    private string _oldText;
    private string _newText;
    private int _caretPosition; // 记录光标位置，撤销时恢复体验更好

    public EditArticleCommand(TMP_InputField inputField, string newText)
    {
        _inputField = inputField;
        _oldText = inputField.text; // 记录当前的旧文本
        _newText = newText;         // 记录即将变成的新文本
        _caretPosition = inputField.caretPosition;
    }

    public void Execute()
    {
        // 执行修改
        _inputField.text = _newText;

        // [埋点] 记录代码触发的文章正文修改 (如导入大纲)
        int delta = _newText.Length - _oldText.Length;
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.Edit_Article_Body,
                targetID: "ArticleModal",
                info: $"Command_Len:{_oldText.Length}->{_newText.Length}",
                value: delta
            );
        }
    }

    public void Undo()
    {
        // 恢复旧文本
        _inputField.text = _oldText;

        // 恢复焦点和光标位置
        if (_inputField.gameObject.activeInHierarchy)
        {
            _inputField.ActivateInputField();
            // 防止光标越界
            _inputField.caretPosition = Mathf.Min(_caretPosition, _oldText.Length);
        }
    }

    public string GetLogInfo()
    {
        return "Edit Article Text";
    }
}