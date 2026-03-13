using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProactiveInterventionSystem : MonoBehaviour
{
    public static ProactiveInterventionSystem Instance;

    [Header("上帝控制台")]
    public bool isProactiveEnabled = true;
    public Toggle proactiveToggleUI;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (proactiveToggleUI != null)
        {
            proactiveToggleUI.isOn = isProactiveEnabled;
            proactiveToggleUI.onValueChanged.AddListener(OnToggleChanged);
            proactiveToggleUI.gameObject.SetActive(false);
        }
    }

    public void OnToggleChanged(bool isOn)
    {
        isProactiveEnabled = isOn;
        Debug.Log($"<color=cyan>[系统开关]</color> AI 辅助引擎已 {(isOn ? "开启" : "关闭")}。");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.O) && proactiveToggleUI != null)
        {
            proactiveToggleUI.gameObject.SetActive(!proactiveToggleUI.gameObject.activeSelf);
        }
    }

    // ==========================================
    // 核心接口：被 UI 或 Tracker 呼叫
    // ==========================================
    public void TriggerInterventionByType(InterventionType type)
    {
        if (type == InterventionType.None) return;
        // 成文区专属路由
        if (type == InterventionType.ArticleGap || type == InterventionType.ArticleReflect)
        {
            GenerateArticleIntervention(type);
            return;
        }

        // 导图区路由
        var selectedNodes = NodeCardManager.Instance.GetSelectedNodes();
        if (selectedNodes.Count == 1)
        {
            GenerateLocalNodeIntervention(selectedNodes[0], type);
        }
        else if (selectedNodes.Count == 0) // 【核心修复：增加全局节点分支】
        {
            GenerateGlobalNodeIntervention(type);
        }
        else
        {
            Debug.LogWarning("[生成器] 选中了多个节点，目前 AI 仅支持单节点或全局生成。");
        }
    }

    // ==========================================
    // 逻辑 1：导图区生成子节点 (局部)
    // ==========================================
    private void GenerateLocalNodeIntervention(BaseNodeController targetNode, InterventionType type)
    {
        if (targetNode == null || targetNode.Data == null) return;

        string cardContent = targetNode.Data.Content ?? "";

        // 【优化】：从文库一键获取标题和 Prompt
        var promptData = AIPromptLibrary.GetNodeInterventionPrompt(type, isGlobal: false);

        string prompt = $"{promptData.RolePrompt}\n当前关注的卡片：【{targetNode.Data.Title} - {cardContent}】\n直接输出生成的内容，不要带任何废话或引号。";

        LLMManager.Instance.TaskChat(prompt, (response, success) =>
        {
            if (success && !string.IsNullOrWhiteSpace(response))
            {
                Vector2 spawnPos = targetNode.transform.localPosition + new Vector3(150, -50, 0);
                var newNode = NodeCardManager.Instance.CreateNodeAt(spawnPos);
                if (newNode is NodeController nc)
                {
                    nc.Data.Title = promptData.Title; // 使用文库给的标题
                    nc.Data.Content = response.Replace("\r", "");
                    nc.RefreshUI();

                    newNode.isCognitiveNode = true;
                    newNode.cognitiveType = type.ToString();

                    if (NodeLinkManager.Instance != null) NodeLinkManager.Instance.CreateConnection(targetNode, newNode);
                    NodeCardManager.Instance.SelectNode(newNode, false);
                }
                if (UserBehaviorSystem.Instance != null)
                    UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Intervention_Triggered, targetID: targetNode.NodeID, info: type.ToString());
            }
        }, false);
    }

    // ==========================================
    // 逻辑 1.5：导图区生成全局节点
    // ==========================================
    private void GenerateGlobalNodeIntervention(InterventionType type)
    {
        if (ProjectContextGatherer.Instance == null) return;

        string fullContext = ProjectContextGatherer.Instance.GetTreeStructureContext();
        if (string.IsNullOrWhiteSpace(fullContext))
        {
            if (ToastSystem.Instance != null) ToastSystem.Instance.Show("当前画布为空，AI 无法生成全局洞察");
            return;
        }

        if (fullContext.Length > 1500) fullContext = fullContext.Substring(0, 1500);

        // 【优化】：从文库获取全局 Prompt
        var promptData = AIPromptLibrary.GetNodeInterventionPrompt(type, isGlobal: true);

        string prompt = $"{promptData.RolePrompt}\n当前全图拓扑大纲：\n{fullContext}\n直接输出生成的内容，不要带任何废话或引号。";

        if (ToastSystem.Instance != null) ToastSystem.Instance.Show("AI 正在洞察全局导图...");

        LLMManager.Instance.TaskChat(prompt, (response, success) =>
        {
            if (success && !string.IsNullOrWhiteSpace(response))
            {
                // ==========================================
                // 【视觉升级 1：计算当前屏幕的正中心坐标】
                // ==========================================
                Vector2 spawnPos = Vector2.zero;

                // 获取装载所有节点的容器 RectTransform
                RectTransform containerRect = NodeCardManager.Instance.cardContainer.GetComponent<RectTransform>();

                // 适配不同的 Canvas 渲染模式
                Canvas parentCanvas = containerRect.GetComponentInParent<Canvas>();
                Camera uiCam = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? parentCanvas.worldCamera : null;

                // 屏幕物理中心点
                Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

                // 将屏幕中心点转换为容器内的本地坐标（完美适应无限画布的缩放和平移）
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(containerRect, screenCenter, uiCam, out Vector2 localCenter))
                {
                    spawnPos = localCenter;
                }

                // ==========================================
                // 【视觉升级 2：生成节点并注入金色呼吸】
                // ==========================================
                var newNode = NodeCardManager.Instance.CreateNodeAt(spawnPos);

                if (newNode is NodeController nc)
                {
                    nc.Data.Title = promptData.Title;
                    // 注意这里同时保留了上一轮加的清洗换行符逻辑
                    nc.Data.Content = response.Replace("\r", "");
                    nc.RefreshUI();

                    newNode.isCognitiveNode = true;
                    newNode.cognitiveType = type.ToString();

                    // 给新诞生的节点注入金色灵魂，持续 20 秒
                    AINodeGlowEffect glow = newNode.gameObject.AddComponent<AINodeGlowEffect>();
                    glow.StartGlow(20f);

                    NodeCardManager.Instance.SelectNode(newNode, false);
                }

                if (UserBehaviorSystem.Instance != null)
                    UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Intervention_Triggered, targetID: "Canvas_Global", info: type.ToString());
            }
        }, false);
    }

    // ==========================================
    // 逻辑 2：成文区生成柔和提示
    // ==========================================
    private void GenerateArticleIntervention(InterventionType type)
    {
        if (ArticleGenerator.Instance == null || ArticleGenerator.Instance.mainBodyInput == null) return;

        string draftText = ArticleGenerator.Instance.mainBodyInput.text;
        int textLength = draftText.Length;
        string currentFocusText = "";
        TMP_InputField input = ArticleGenerator.Instance.mainBodyInput;

        int stage = 0; // 0=冷启动, 1=宏观失焦, 2=微观聚焦

        if (textLength < 10)
        {
            stage = 0;
            currentFocusText = "（当前正文区完全空白，用户处于起始发呆状态）";
            Debug.Log($"<color=yellow>[AI 冷启动抓取]</color> 当前无文字，触发破冰引导！");
        }
        else if (!input.isFocused)
        {
            stage = 1;
            int extractLength = Mathf.Min(textLength, 1500);
            currentFocusText = draftText.Substring(0, extractLength);
            Debug.Log($"<color=cyan>[AI 全局抓取]</color> 用户失焦，触发全文宏观审视！分析字数: {extractLength}");
        }
        else
        {
            stage = 2;
            int cursorIndex = input.selectionFocusPosition;
            if (cursorIndex <= 0) cursorIndex = 0;
            if (cursorIndex > textLength) cursorIndex = textLength;

            // 【核心逻辑】：判断光标是否在文章末尾（容差 15 个字符，允许末尾有几个换行或标点）
            bool isAtEnd = (textLength - cursorIndex) <= 15;

            if (isAtEnd)
            {
                // 推进状态：只抓取上文
                int extractLength = Mathf.Min(cursorIndex, 300);
                string beforeText = draftText.Substring(cursorIndex - extractLength, extractLength);
                currentFocusText = $"【当前光标前的上文】：\n{beforeText}";

                Debug.Log($"<color=yellow>[AI 局部抓取 - 推进模式]</color> 光标在末尾。");
            }
            else
            {
                // 缝合状态：抓取上下文
                int extractBefore = Mathf.Min(cursorIndex, 200); // 往前抓 200
                int extractAfter = Mathf.Min(textLength - cursorIndex, 200); // 往后抓 200

                string beforeText = draftText.Substring(cursorIndex - extractBefore, extractBefore);
                string afterText = draftText.Substring(cursorIndex, extractAfter);

                currentFocusText = $"【光标前的上文】：\n{beforeText}\n\n【光标后的下文】：\n{afterText}";

                // 为了让文库知道这是中间插入，我们把 stage 临时设为 3 (代表缝合模式)
                stage = 3;
                Debug.Log($"<color=yellow>[AI 局部抓取 - 缝合模式]</color> 光标在段落中间。");
            }
        }

        // 【优化】：从文库获取成文区 Prompt
        bool hasNodes = (NodeCardManager.Instance != null && NodeCardManager.Instance.GetAllNodes().Count > 0);
        string systemRole = AIPromptLibrary.GetArticleInterventionPrompt(type, stage, hasNodes);

        // 组装最终 Prompt
        string prompt = $@"{systemRole}
【当前阅读的草稿内容】：
{currentFocusText}

{AIPromptLibrary.Article_Output_Rules}";

        LLMManager.Instance.TaskChat(prompt, (response, success) =>
        {
            if (success && !string.IsNullOrWhiteSpace(response) && ArticleGenerator.Instance.articlePromptInput != null)
            {
                string cleanResponse = response.Trim().Replace("\n", "").Replace("\r", "");
                ArticleGenerator.Instance.StartPromptBreathing(type.ToString(), cleanResponse, 60f);

                if (UserBehaviorSystem.Instance != null)
                {
                    string stageInfo = stage == 0 ? "Stage0_ColdStart" : (stage == 1 ? "Macro_Global" : "Micro_Local");
                    UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Intervention_Triggered, targetID: "Article", info: $"{type.ToString()}_{stageInfo}");
                }
            }
        }, false);
    }
}