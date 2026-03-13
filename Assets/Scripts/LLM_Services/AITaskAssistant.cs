using UnityEngine;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json; // 【必须引入】用于解析 AI 返回的 JSON

// ==========================================
// 承载 AI 解析结果的数据结构
// ==========================================
[System.Serializable]
public class AITreeNodeData
{
    public string title;
    public string content;
    public List<AITreeNodeData> children = new List<AITreeNodeData>();
}

[System.Serializable]
public class AITreeRootData
{
    public string rootTitle;
    public string rootContent;
    public List<AITreeNodeData> children = new List<AITreeNodeData>();
}

public class AITaskAssistant : MonoBehaviour
{
    public static AITaskAssistant Instance;

    void Awake()
    {
        Instance = this;
    }

    // ==========================================
    // 1. 生成节点标题 (Node Title)
    // ==========================================
    public void GenerateTitle(string content, System.Action<string> onComplete)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        string prompt = $"你是一个笔记助手。请将以下内容提炼为一个不超过10个字的标题。只返回标题文字，不要标点：\n\n{content}";

        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.TaskChat(prompt, (response, success) =>
            {
                if (success) onComplete?.Invoke(response.Trim());
            });
        }
    }

    // ==========================================
    // 2. 苏格拉底式追问 (Socratic Questioning)
    // ==========================================
    public void GenerateSocraticQuestion(string userContent, System.Action<string> onComplete)
    {
        if (string.IsNullOrWhiteSpace(userContent)) return;

        string prompt = $"你是一个苏格拉底式的导师。用户正在写这个观点：【{userContent}】。\n" +
                        $"请针对这个观点，提出一个能引发深层思考的简短问题。要求：\n" +
                        $"1. 不要解释，直接给问题。\n" +
                        $"2. 请提供 30-50 字的具体追问。不要只问'为什么'，要指出该观点可能忽视的一个具体现实因素，引导用户深入。\n" +
                        $"3. 语气要引导而非质问。";

        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.TaskChat(prompt, (response, success) =>
            {
                if (success) onComplete?.Invoke(response.Trim());
            }, true);
        }
    }

    // ==========================================
    // 3. 反向论证 (Counter-Argument)
    // ==========================================
    public void GenerateCounterArgument(string userContent, System.Action<string> onComplete)
    {
        if (string.IsNullOrWhiteSpace(userContent)) return;

        string prompt = $"你是一个批判性思维辩手。用户提出了这个观点：【{userContent}】。\n" +
                        $"请提供一个强有力的反面观点或反例，以测试该观点的严密性。要求：\n" +
                        $"1. 直接给出反驳内容。\n" +
                        $"2. 请提供 40-60 字的详细反驳。必须包含一个具体的反例或数据维度来挑战用户的观点，切忌假大空。\n" +
                        $"3. 语气客观犀利。";

        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.TaskChat(prompt, (response, success) =>
            {
                if (success) onComplete?.Invoke(response.Trim());
            }, true);
        }
    }

    // ==========================================
    // 4. 解答与阐述 (Elaboration)
    // ==========================================
    public void GenerateElaboration(string userContent, System.Action<string> onComplete)
    {
        if (string.IsNullOrWhiteSpace(userContent)) return;

        string prompt = $"你是一个知识渊博的专家。用户正在写这个观点或问题：【{userContent}】。\n" +
                        $"请针对这个内容，给出一个清晰、简明的解答或补充阐述。要求：\n" +
                        $"1. 不要解释你是谁，直接给出答案或核心补充。\n" +
                        $"2. 请提供 60-80 字的专业解答。要有具体的机制解释或例子支撑，让用户看完能直接作为素材使用。\n" +
                        $"3. 语气要客观、专业、有启发性。";

        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.TaskChat(prompt, (response, success) =>
            {
                if (success) onComplete?.Invoke(response.Trim());
            }, true);
        }
    }

    // ==========================================
    // 5. 【核心新增】聊天文本语义解构 (Text to JSON Tree)
    // ==========================================
    public void ExtractTextToTreeData(string rawText, System.Action<AITreeRootData> onComplete)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            onComplete?.Invoke(null);
            return;
        }

        // 强硬的系统级 Prompt：从“切割工”升级为“逻辑重构架构师”
        string prompt = "你是一个顶级的“思维导图架构师”和“逻辑提炼大师”。请将下面这段可能是口语化或扁平化的自然语言，【重构】并【提炼】为结构极其严谨的思维导图JSON，绝不仅仅是按句号拆分！\n" +
                        "【核心重构规则】：\n" +
                        "1. **全局概括为根**：`rootTitle` 和 `rootContent` 必须是整段话的【最高层级总结】，绝对不能直接照抄第一句话！\n" +
                        "2. **自主归纳分类（最重要）**：如果原文是一段扁平的文字，你必须自主提炼出逻辑维度（例如：外貌特征、性格特点、栖息地、历史背景等）作为第一级 `children`。然后再将具体的细节作为第二级子节点往下挂。\n" +
                        "3. **信息脱水（去废话）**：严格删除所有口语化代词（如“它们”、“他”、“这”）、过渡转折词（如“因此”、“所以”、“另外”、“但是”、“近年来”）。节点内容必须是纯粹的知识点或客观概念。\n" +
                        "4. **长短文分流**：字数极少（少于10个字）的词语只填 `content`，`title`留空（即 `\"title\": \"\"`）；超过10字的信息填 `content`，并为其浓缩一个2-6字的 `title`。\n" +
                        "5. 只输出纯 JSON 字符串，绝不要包含 ```json 标记，不要任何解释。\n\n" +
                        "【JSON 数据结构完美示范（仔细体会如何归纳维度和删除废话）】：\n" +
                        "// 假设原文是：“卡皮巴拉是水豚的音译...它们长得很萌...水豚性格好，很佛系，因此在网络上很火。它们主要生活在南美洲。”\n" +
                        "{\n" +
                        "  \"rootTitle\": \"水豚 (卡皮巴拉)\",\n" +
                        "  \"rootContent\": \"一种体型庞大、性格温和且在网络上极具人气的啮齿类动物\",\n" +
                        "  \"children\": [\n" +
                        "    {\n" +
                        "      \"title\": \"生理特征\",\n" +
                        "      \"content\": \"体型较大的啮齿类动物，体长可达1米以上，体重35-66千克\",\n" +
                        "      \"children\": [\n" +
                        "        { \"title\": \"\", \"content\": \"身体圆滚滚，外表呆萌\", \"children\": [] },\n" +
                        "        { \"title\": \"\", \"content\": \"四肢短小，鼻孔朝天\", \"children\": [] }\n" +
                        "      ]\n" +
                        "    },\n" +
                        "    {\n" +
                        "      \"title\": \"性格与文化\",\n" +
                        "      \"content\": \"性格温和、情绪稳定，常被形容为佛系代表\",\n" +
                        "      \"children\": [\n" +
                        "        { \"title\": \"\", \"content\": \"因独特外貌和性格成为网络文化符号，被称作松弛感大师\", \"children\": [] }\n" +
                        "      ]\n" +
                        "    },\n" +
                        "    {\n" +
                        "      \"title\": \"栖息环境\",\n" +
                        "      \"content\": \"主要生活在南美洲\",\n" +
                        "      \"children\": []\n" +
                        "    }\n" +
                        "  ]\n" +
                        "}\n\n" +
                        $"【需要重构解析的真实文本】：\n{rawText}";

        if (LLMManager.Instance != null)
        {
            LLMManager.Instance.TaskChat(prompt, (response, success) =>
            {
                if (success && !string.IsNullOrWhiteSpace(response))
                {
                    try
                    {
                        // 清洗大模型可能带有的 Markdown 代码块标记
                        string cleanJson = response.Replace("```json", "").Replace("```", "").Trim();

                        // 反序列化为我们的数据结构
                        AITreeRootData treeData = JsonConvert.DeserializeObject<AITreeRootData>(cleanJson);
                        onComplete?.Invoke(treeData);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[AI解析 JSON 失败]: {e.Message}\nRaw Response: {response}");
                        onComplete?.Invoke(null);
                    }
                }
                else
                {
                    onComplete?.Invoke(null);
                }
            }, false); // 纯文本解析，不需要注入画布大纲
        }
    }
}