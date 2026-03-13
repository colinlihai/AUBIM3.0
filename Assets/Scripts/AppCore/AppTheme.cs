using UnityEngine;

[CreateAssetMenu(fileName = "NewAppTheme", menuName = "AUBIM/App Theme")]
public class AppTheme : ScriptableObject
{
    [Header("=== 节点卡片 (Node Card) ===")]
    public Color nodeNormal = Color.white;
    public Color nodeSelected = new Color(0.9f, 0.95f, 1f, 1f); // 淡淡的蓝白
    public Color nodeHover = new Color(0.95f, 0.95f, 0.95f, 1f);
    public Color nodeOutline = new Color(0.2f, 0.6f, 1f, 1f);   // 选中时的边框色

    [Header("=== 交互反馈 (Feedback) ===")]
    [Tooltip("当我拖着卡片想变成别人的子节点时，我自己的颜色")]
    public Color adsorbSelf = new Color(0.8f, 1f, 0.8f, 0.8f);
    [Tooltip("当别人想吸附到我身上时，我的颜色")]
    public Color adsorbTarget = new Color(0.5f, 1f, 0.5f, 0.8f);
    public Color reorderTarget = new Color(1f, 0.9f, 0.9f, 1f);
    [Tooltip("要断开连接时的红色警示")]
    public Color detachAlert = new Color(1f, 0.8f, 0.8f, 0.8f);

    [Header("=== 连线 (Connection) ===")]
    public Color lineColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Header("=== AI 聊天 (Chat) ===")]
    public Color chatUserBubble = new Color(0.2f, 0.6f, 1f, 1f);
    public Color chatUserText = Color.white;
    public Color chatAIBubble = new Color(0.9f, 0.9f, 0.9f, 1f); // 浅灰
    public Color chatAIText = Color.black;

    [Header("=== 认知辅助 (Cognitive Support) ===")]
    [Tooltip("苏格拉底追问节点的背景色 (建议: 浅黄色/思考色)")]
    public Color questionNodeColor = new Color(1f, 1f, 0.8f, 1f);
    [Tooltip("反向论证节点的背景色 (建议: 浅红色/警示色)")]
    public Color counterNodeColor = new Color(1f, 0.85f, 0.85f, 1f);

    [Header("=== 逻辑检测 (Logic Check) ===")]
    [Tooltip("逻辑缺口提示卡的背景色 (建议: 鲜艳的淡红/警示色)")]
    public Color logicGapCardColor = new Color(1f, 0.8f, 0.8f, 1f);

    [Tooltip("内容不一致/离题节点的背景色 (建议: 深红/高亮警告)")]
    public Color outlierNodeColor = new Color(1f, 0.6f, 0.6f, 1f);


    [Header("=== 存档列表 (File List) ===")]
    public Color fileItemNormal = new Color(1f, 1f, 1f, 0f);
    public Color fileItemHover = new Color(1f, 1f, 1f, 0.1f);
    public Color fileItemSelected = new Color(0.2f, 0.6f, 1f, 0.3f);
}