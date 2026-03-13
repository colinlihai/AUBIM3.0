using UnityEngine;
using System;

public static class AIChangeDetector
{
    // --- 配置阈值 ---

    // 阶段1：初次触发（冷启动）
    private const int FIRST_TRIGGER_LENGTH = 15; // 第一次：必须写满 20 个字才开始拟题

    // 阶段2：后续触发（增量更新）
    private const int UPDATE_LENGTH_DIFF = 10;   // 字数显著变化：长度差 > 15
    private const int UPDATE_CONTENT_DIFF = 6;  // 内容显著重写：编辑距离 > 10 (即使字数一样，但这10个字不一样)

    /// <summary>
    /// 判断是否需要触发 AI (包含初次和后续的逻辑分支)
    /// </summary>
    /// <param name="lastContent">上一次生成时的快照</param>
    /// <param name="newContent">当前内容</param>
    /// <param name="hasTriggeredBefore">是否已经由 AI 生成过一次</param>
    public static bool IsChangeSignificant(string lastContent, string newContent, bool hasTriggeredBefore)
    {
        // 0. 基础判空
        if (string.IsNullOrWhiteSpace(newContent)) return false;

        // =========================================================
        // 分支 A: 初次生成 (用户刚创建卡片，正在疯狂打字)
        // =========================================================
        if (!hasTriggeredBefore)
        {
            // 逻辑：只有当字数累计超过 15 个字时，才第一次触发
            if (newContent.Length >= FIRST_TRIGGER_LENGTH)
            {
                Debug.Log($"[AI Check] 初次触发达成：字数 {newContent.Length} >= {FIRST_TRIGGER_LENGTH}");
                return true;
            }
            return false;
        }

        // =========================================================
        // 分支 B: 后续更新 (已经是成熟的卡片，用户在修修补补)
        // =========================================================

        // 1. 完全一样直接跳过
        if (lastContent == newContent) return false;

        // 2. 判定条件一：字数变化量大 (暴增或暴减)
        int lenDiff = Mathf.Abs(newContent.Length - lastContent.Length);
        if (lenDiff > UPDATE_LENGTH_DIFF)
        {
            Debug.Log($"[AI Check] 显著长度变化：Diff {lenDiff} > {UPDATE_LENGTH_DIFF}");
            return true;
        }

        // 3. 判定条件二：内容重写度大 (字数可能没变，但字换了)
        // 使用编辑距离算法计算
        int editDist = LevenshteinDistance(lastContent, newContent);
        if (editDist > UPDATE_CONTENT_DIFF)
        {
            Debug.Log($"[AI Check] 显著内容重写：EditDistance {editDist} > {UPDATE_CONTENT_DIFF}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 计算两个字符串的编辑距离 (Levenshtein Distance)
    /// 表示将 s 变为 t 需要的最少单字符编辑（插入、删除、替换）次数
    /// </summary>
    public static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }
}