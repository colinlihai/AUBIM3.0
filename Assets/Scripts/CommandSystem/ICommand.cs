public interface ICommand
{
    void Execute(); // 执行逻辑
    void Undo();    // 撤销逻辑
    string GetLogInfo(); // 返回给行为系统的日志描述 (如 "MoveNode: ID_123")
}