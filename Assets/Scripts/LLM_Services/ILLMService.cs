using System;
using System.Collections;
using System.Collections.Generic;

// [新增] 标准消息结构 (对应 OpenAI/Qwen 的 JSON 格式)
[Serializable]
public class ChatMessage
{
    public string role;    // "system", "user", "assistant"
    public string content;

    public ChatMessage(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}

// 定义一个通用的回调委托
public delegate void LLMCallback(string responseText, bool isSuccess);

public interface ILLMService
{
    // 初始化 (设置 Key 等)
    void Init();
    IEnumerator PostRequest(List<ChatMessage> messages, LLMCallback callback);
}