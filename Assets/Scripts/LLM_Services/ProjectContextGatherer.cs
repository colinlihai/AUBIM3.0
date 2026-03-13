using UnityEngine;
using System.Text;
using System.Linq;
using System.Collections.Generic;

public class ProjectContextGatherer : MonoBehaviour
{
    public static ProjectContextGatherer Instance;

    void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 获取当前项目的完整上下文快照，注入给大模型作为 System Prompt
    /// </summary>
    public string GetSystemPromptWithContext()
    {
        StringBuilder sb = new StringBuilder();

        // 1. 设定 AI 的角色 (顾问模式)
        sb.AppendLine(AIPromptLibrary.ContextGatherer_BaseRole);
        sb.AppendLine("--------------------");

        // 2. 注入全局项目结构概览 (3.0 核心：完整的树状大纲)
        sb.AppendLine("【全局项目结构概览】(反映了用户当前的逻辑大纲):");
        string treeContext = GetTreeStructureContext();
        if (string.IsNullOrWhiteSpace(treeContext))
        {
            sb.AppendLine("(当前画布为空)");
        }
        else
        {
            sb.AppendLine(treeContext);
        }
        sb.AppendLine("--------------------");

        // 3. 注入当前选中的节点 (如果用户框选了某几个节点，说明他大概率是在针对这几个节点提问)
        if (NodeCardManager.Instance != null)
        {
            var selectedNodes = NodeCardManager.Instance.GetSelectedNodes();
            if (selectedNodes.Count > 0)
            {
                sb.AppendLine("【用户当前正在选中的/高亮的卡片】(请优先针对这些节点的内容提供建议):");
                foreach (var node in selectedNodes)
                {
                    string title = string.IsNullOrWhiteSpace(node.Data.Title) ? "无标题" : node.Data.Title;
                    sb.AppendLine($"- 标题: {title} (ID: {node.NodeID})");
                    if (!string.IsNullOrWhiteSpace(node.Data.Content))
                    {
                        sb.AppendLine($"  内容: {node.Data.Content}");
                    }
                }
                sb.AppendLine("--------------------");
            }
        }

        sb.AppendLine("请简明扼要地回答用户的问题。如果用户要求分析逻辑、寻找缺口或提供结构优化建议，请直接基于上述大纲进行具体指出。");

        return sb.ToString();
    }

    // ==========================================
    // 3.0 核心：后台静默遍历整个画布的拓扑结构
    // ==========================================
    public string GetTreeStructureContext()
    {
        if (NodeCardManager.Instance == null) return "";

        var allNodes = NodeCardManager.Instance.GetAllNodes();
        if (allNodes.Count == 0) return "";

        // 1. 找出所有没有父节点的“树根”
        var rootNodes = allNodes.Where(n => n.parentNode == null).ToList();

        // 2. 按照 Y 轴坐标从上到下排序
        rootNodes.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));

        StringBuilder sb = new StringBuilder();

        // 3. 逐棵树进行 DFS 遍历
        foreach (var root in rootNodes)
        {
            AppendNodeContextDFS(root, sb, 0);
        }

        return sb.ToString().Trim();
    }

    private void AppendNodeContextDFS(BaseNodeController node, StringBuilder sb, int depth)
    {
        if (node == null || !node.gameObject.activeSelf) return;

        // 根据深度生成 Markdown 标题符
        string prefix = new string('#', Mathf.Min(depth + 1, 6));
        string title = string.IsNullOrWhiteSpace(node.Data.Title) ? "无标题" : node.Data.Title;

        // 我们把 ID 也附加上，这样 AI 如果要建议你改某个节点，可以明确说出节点标题
        sb.AppendLine($"{prefix} {title}");

        // 为了防止 Token 爆炸，如果你觉得正文太长，可以只截取前 100 个字
        if (!string.IsNullOrWhiteSpace(node.Data.Content))
        {
            string contentSnippet = node.Data.Content.Length > 150
                ? node.Data.Content.Substring(0, 150) + "..."
                : node.Data.Content;
            sb.AppendLine($"  {contentSnippet}");
        }

        // 递归处理子节点
        if (node.childNodes != null && node.childNodes.Count > 0)
        {
            var sortedChildren = new List<BaseNodeController>(node.childNodes);
            sortedChildren.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));

            foreach (var child in sortedChildren)
            {
                AppendNodeContextDFS(child, sb, depth + 1);
            }
        }
    }
}