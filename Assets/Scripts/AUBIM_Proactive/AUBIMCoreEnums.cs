using UnityEngine;

/// <summary>
/// AUBIM 4.0 核心认知干预意图枚举
/// 严格映射到 UI 上的 9 个具体的工具按钮
/// </summary>
public enum InterventionType
{
    None = 0,

    // --- 导图区认知干预 (Canvas Area) ---
    Socratic,       // 反问 (深度追问)
    Counter,        // 解释 (反向思考)
    Elaborate,      // 追问 (知识延展)
    GlobalInsight,  // 全局思考 (全局洞察)

    // --- 成文区认知干预 (Article Area) ---
    ArticleDraft,   // 全文起草
    ArticleRefine,  // 局部润色
    ArticleExpand,  // 顺势续写
    ArticleStitch,  // 内容衔接
    ArticleReview   // 审稿意见
}