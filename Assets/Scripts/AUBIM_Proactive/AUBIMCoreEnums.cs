using UnityEngine;

/// <summary>
/// AUBIM 3.0 全局核心枚举定义
/// </summary>
public enum InterventionType
{
    None = 0,

    // --- 导图区认知干预 ---
    Socratic,       // 深度追问 (原 proactive_socratic)
    Counter,        // 反向思考 (原 proactive_counter)
    Elaborate,      // 知识延展/升华 (原 proactive_elaborate)

    // --- 成文区认知干预 ---
    ArticleGap,     // 查缺补漏 (原 article_gap)
    ArticleReflect  // 全局反思 (原 article_reflect)
}