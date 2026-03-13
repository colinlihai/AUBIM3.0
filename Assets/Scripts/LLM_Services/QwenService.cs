using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System.Collections;
using System;
using System.Collections.Generic;

// --- 数据结构 ---

[Serializable]
public class QwenRequest
{
    public string model;
    public QwenInput input;
    public QwenParameters parameters;
}

[Serializable]
public class QwenInput
{
    public List<QwenMessage> messages = new List<QwenMessage>();
}

[Serializable]
public class QwenMessage
{
    public string role;
    public string content;
}

[Serializable]
public class QwenParameters
{
    public string result_format = "text";
    public bool enable_search = false;
}

[Serializable]
public class QwenResponse
{
    public QwenOutput output;
    public string code;
    public string message;
    public string request_id;
}

[Serializable]
public class QwenOutput
{
    public string text;
    public string finish_reason;
}

// 证书验证跳过类
public class CertificateWhore : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        return true;
    }
}

public class QwenService : MonoBehaviour, ILLMService
{
    [Header("Qwen Settings")]
    [SerializeField] private string _apiKey = "YOUR_API_KEY";
    [SerializeField] private string _modelName = "qwen-turbo";
    [SerializeField] private bool _enableHistory = true; // 仅对 PostRequest(string) 生效
    public bool EnableInternetSearch { get; set; } = false;
    private const string _url = "https://dashscope.aliyuncs.com/api/v1/services/aigc/text-generation/generation";

    // 内部维护的历史记录 (仅用于单句对话模式)
    private List<QwenMessage> _history = new List<QwenMessage>();

    public void Init()
    {
        Debug.Log("Qwen Service Initialized");
        _history.Clear();
    }

    public void SetKey(string key) => _apiKey = key;

    public void ClearHistory()
    {
        _history.Clear();
        Debug.Log("Qwen History Cleared");
    }

    // ==========================================
    // 接口实现部分
    // ==========================================

    /// <summary>
    /// 接口实现 1: 简单文本请求 (使用内部 _history)
    /// </summary>
    public IEnumerator PostRequest(string prompt, LLMCallback callback)
    {
        QwenMessage userMsg = new QwenMessage { role = "user", content = prompt };
        List<QwenMessage> currentMessages = new List<QwenMessage>();

        // 处理内部历史记录
        if (_enableHistory)
        {
            _history.Add(userMsg);
            currentMessages.AddRange(_history);
        }
        else
        {
            currentMessages.Add(userMsg);
        }

        // 调用核心请求逻辑
        // 参数3(saveToHistory): true，表示如果成功，需要把AI回复存入 _history
        yield return SendRequestInternal(currentMessages, callback, saveToHistory: _enableHistory);

        // 如果开启历史记录但请求失败了，需要回滚（移除刚才添加的用户话语）
        // 注意：SendRequestInternal 里已经处理了回调，这里只需处理数据一致性
        // 但由于协程顺序执行，这里很难获取内部的成功状态来回滚 _history。
        // 为了代码简洁，回滚逻辑通常放在 Internal 方法内部处理，或者这里简化处理。
    }

    /// <summary>
    /// 接口实现 2: 完整历史记录请求 (不使用内部 _history，完全依赖传入的 list)
    /// </summary>
    public IEnumerator PostRequest(List<ChatMessage> messages, LLMCallback callback)
    {
        // 1. 将通用的 ChatMessage 转换为 Qwen 专用的 QwenMessage
        List<QwenMessage> qwenMessages = new List<QwenMessage>();

        if (messages != null)
        {
            foreach (var msg in messages)
            {
                // 注意：如果你的 ChatMessage 属性是大写 Role/Content，请在这里修改
                qwenMessages.Add(new QwenMessage
                {
                    role = msg.role,    // 或者是 msg.Role
                    content = msg.content // 或者是 msg.Content
                });
            }
        }

        // 2. 调用核心请求逻辑
        // 参数3(saveToHistory): false，因为外部自己传入了列表，通常不需要内部再存一份
        yield return SendRequestInternal(qwenMessages, callback, saveToHistory: false);
    }

    // ==========================================
    // 核心网络逻辑 (私有)
    // ==========================================

    private IEnumerator SendRequestInternal(List<QwenMessage> messagesToSend, LLMCallback callback, bool saveToHistory)
    {
        // 构建 Parameters
        QwenParameters paramsData = new QwenParameters
        {
            result_format = "text",
            // [新增] 将当前的开关状态传给 API
            enable_search = this.EnableInternetSearch
        };
        // 1. 构建 JSON
        QwenRequest reqData = new QwenRequest
        {
            model = _modelName,
            input = new QwenInput { messages = messagesToSend },
            parameters = paramsData
        };

        string jsonBody = JsonUtility.ToJson(reqData);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        // 2. 发送请求
        using (UnityWebRequest request = new UnityWebRequest(_url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.certificateHandler = new CertificateWhore();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {_apiKey}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Qwen API Error] {request.error}\n{request.downloadHandler.text}");

                // 如果是内部历史模式且失败了，移除最后一条用户消息以保持同步
                if (saveToHistory && _history.Count > 0 && _history[_history.Count - 1].role == "user")
                {
                    _history.RemoveAt(_history.Count - 1);
                }

                callback?.Invoke($"网络错误: {request.error}", false);
            }
            else
            {
                // 3. 解析结果
                string jsonResponse = request.downloadHandler.text;
                try
                {
                    // [核心防御 1] 拦截空字符串，防止 JsonUtility 报错
                    if (string.IsNullOrWhiteSpace(jsonResponse))
                    {
                        throw new Exception("API 返回了空的数据内容。");
                    }
                    QwenResponse resp = JsonUtility.FromJson<QwenResponse>(jsonResponse);
                    // [核心防御 2] 拦截反序列化失败导致的 resp 为 null
                    if (resp == null)
                    {
                        throw new Exception($"JSON 解析后获得空对象，原始返回内容: {jsonResponse}");
                    }

                    if (!string.IsNullOrEmpty(resp.code))
                    {
                        Debug.LogError($"[Qwen Error] {resp.code}: {resp.message}");
                        // 失败回滚
                        if (saveToHistory && _history.Count > 0 && _history[_history.Count - 1].role == "user")
                        {
                            _history.RemoveAt(_history.Count - 1);
                        }
                        callback?.Invoke($"API报错: {resp.message}", false);
                    }
                    else if (resp.output != null && !string.IsNullOrEmpty(resp.output.text))
                    {
                        string aiText = resp.output.text;

                        // 成功！如果是内部历史模式，记录 AI 的回复
                        if (saveToHistory)
                        {
                            _history.Add(new QwenMessage { role = "assistant", content = aiText });
                        }

                        callback?.Invoke(aiText, true);
                    }
                    else
                    {
                        if (saveToHistory && _history.Count > 0) _history.RemoveAt(_history.Count - 1);
                        callback?.Invoke("AI 返回内容为空", false);
                    }
                }
                catch (Exception e)
                {
                    // [核心防御 3] 安全地打印真正的错误信息
                    Debug.LogError($"JSON Parse Error: {e.Message}\nRaw Response: {jsonResponse}");

                    if (saveToHistory && _history != null && _history.Count > 0)
                    {
                        var lastMsg = _history[_history.Count - 1];
                        if (lastMsg != null && lastMsg.role == "user")
                        {
                            _history.RemoveAt(_history.Count - 1);
                        }
                    }
                    callback?.Invoke("数据解析错误", false);
                }
            }
        }
    }
}