using System.Collections.Generic;

//暂未启用，打包重做功能
public class CompositeCommand : ICommand
{
    private List<ICommand> _commands = new List<ICommand>();
    private string _logInfo;

    public CompositeCommand(string logInfo = "Composite Command")
    {
        _logInfo = logInfo;
    }

    public void AddCommand(ICommand cmd)
    {
        _commands.Add(cmd);
    }

    public void Execute()
    {
        // 顺序执行
        foreach (var cmd in _commands)
        {
            cmd.Execute();
        }
    }

    public void Undo()
    {
        // 倒序撤销 (这一点很重要，虽然对生成卡片可能没区别，但对其他操作很关键)
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }

    public string GetLogInfo()
    {
        return $"{_logInfo} ({_commands.Count} items)";
    }
}