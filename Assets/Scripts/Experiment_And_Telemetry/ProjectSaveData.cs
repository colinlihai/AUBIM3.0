using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class ProjectSaveData
{
    public string SaveName;
    public string Timestamp;
    public string Version = "1.0";

    // 文章生成器的状态
    public string ArticleContext; // 原始拼接内容
    public string ArticleDraft;   // 最终编辑内容
    public string AISuggestion;   // 右侧 AI 建议内容

    // 所有的节点数据 (拍扁了存)
    public List<NodeSaveDTO> Nodes = new List<NodeSaveDTO>();
    public List<ChatMessageData> ChatHistory = new List<ChatMessageData>();
}

[System.Serializable]
public class ChatMessageData
{
    public string role; // "user" or "assistant"
    public string content;
    public string timestamp;
}

[Serializable]
public class NodeSaveDTO
{
    public string ID;
    public string Type; // "NodeCard", "LeafCard", "GroupCard"

    // 内容数据
    public string Title;
    public string Content;

    // 空间位置 (仅 NodeCard 需要，Leaf/Group 由布局决定)
    public Vector2 AnchoredPosition;
    public float Width;  // 记录 Resize 后的宽度
    public float Height;

    // 结构关系
    public string ParentID;      // 父亲的 ID (如果是根节点则为 "null")
    public int SiblingIndex;     // 排位
    public string ContainerType; // "Canvas" 或 "HarvestArea"
}