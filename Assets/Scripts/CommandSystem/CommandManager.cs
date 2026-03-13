using UnityEngine;
using System.Collections.Generic;

public class CommandManager : MonoBehaviour
{
    public static CommandManager Instance;

    // 双栈设计：Undo 栈和 Redo 栈
    private Stack<ICommand> _undoStack = new Stack<ICommand>();
    private Stack<ICommand> _redoStack = new Stack<ICommand>();

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Update()
    {
        // 监听快捷键 (Ctrl + Z = 撤销, Ctrl + Y = 重做)
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
            }
            else if (Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
            }
        }
    }

    // --- 核心方法 1: 执行新命令 ---
    // 所有会改变数据的操作，都必须通过这个方法调用，而不是直接执行
    public void ExecuteCommand(ICommand command)
    {
        // 1. 执行命令
        command.Execute();

        // 2. 入栈
        _undoStack.Push(command);

        // 3. 发生新操作时，Redo 栈必须清空 (历史分支被切断)
        _redoStack.Clear();
    }

    // --- 核心方法 2: 撤销 ---
    public void Undo()
    {
        if (_undoStack.Count > 0)
        {
            ICommand cmd = _undoStack.Pop();
            cmd.Undo();
            _redoStack.Push(cmd);

            // [新增] Toast 提示
            if (ToastSystem.Instance != null)
                ToastSystem.Instance.Show($"已撤销");
            Debug.Log($"[Undo] 撤销了: {cmd.GetLogInfo()}");

            // [埋点] 记录撤销行为
            // 特征意义：高频撤销 = 负向反馈/迷茫
            if (UserBehaviorSystem.Instance != null)
            {
                UserBehaviorSystem.Instance.LogEvent(
                    BehaviorEventType.Action_Undo,
                    targetID: "System",
                    info: cmd.GetLogInfo(), // 记录撤销了什么命令
                    value: 1 // 计数
                );
            }
        }
    }

    // --- 核心方法 3: 重做 ---
    public void Redo()
    {
        if (_redoStack.Count > 0)
        {
            ICommand cmd = _redoStack.Pop();
            cmd.Execute();
            _undoStack.Push(cmd);

            // [新增] Toast 提示
            if (ToastSystem.Instance != null)
                ToastSystem.Instance.Show($"已重做");

            Debug.Log($"[Redo] 重做了: {cmd.GetLogInfo()}");

            // [埋点] 记录重做行为
            if (UserBehaviorSystem.Instance != null)
            {
                UserBehaviorSystem.Instance.LogEvent(
                    BehaviorEventType.Action_Redo,
                    targetID: "System",
                    info: cmd.GetLogInfo(),
                    value: 1
                );
            }
        }
    }
}