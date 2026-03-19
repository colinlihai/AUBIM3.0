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
    // 逻辑 1：导图区生成子节点 (局部聚焦 + 全局视野)
    // ==========================================
    private void GenerateLocalNodeIntervention(BaseNodeController targetNode, InterventionType type)
    {
        if (targetNode == null || targetNode.Data == null) return;

        string cardContent = targetNode.Data.Content ?? "";

        // ==========================================
        // 【核心升级】：收集全局上下文 (Global Context)
        // ==========================================
        System.Text.StringBuilder bgBuilder = new System.Text.StringBuilder();
        if (NodeCardManager.Instance != null)
        {
            var allNodes = NodeCardManager.Instance.GetAllNodes();
            foreach (var node in allNodes)
            {
                // 排除当前选中的节点，排除隐藏的无效节点
                if (node != targetNode && node.gameObject.activeInHierarchy && node.Data != null)
                {
                    string safeTitle = string.IsNullOrEmpty(node.Data.Title) ? "无标题" : node.Data.Title;
                    string content = node.Data.Content ?? "";
                    // 限制长度，提取前 100 字作为摘要，防止 Token 爆炸
                    string safeContent = content.Length > 100 ? content.Substring(0, 100) + "..." : content;
                    bgBuilder.AppendLine($"- {safeTitle}：{safeContent}");
                }
            }
        }

        string globalContext = bgBuilder.ToString();
        if (string.IsNullOrWhiteSpace(globalContext))
        {
            globalContext = "（当前无其他背景节点，这是用户的思考起点）";
        }

        // 【保持】：从文库一键获取标题和基础指令
        var promptData = AIPromptLibrary.GetNodeInterventionPrompt(type, isGlobal: false);

        string prompt = $@"{promptData.RolePrompt}
【全局导图背景】(仅作为你理解逻辑上下文的参考，绝不要重复输出)：
{globalContext}

【用户当前关注的核心卡片】：
标题：{targetNode.Data.Title}
内容：{cardContent}

【你的任务】：结合全局背景，针对用户当前关注的核心卡片执行你的引导任务。请直接输出生成的内容，不要带任何废话、前缀标签或引号。";

        LLMManager.Instance.TaskChat(prompt, (response, success) =>
        {
            if (success && !string.IsNullOrWhiteSpace(response))
            {
                Vector2 spawnPos = targetNode.transform.localPosition + new Vector3(150, -50, 0);
                var newNode = NodeCardManager.Instance.CreateNodeAt(spawnPos);
                if (newNode is NodeController nc)
                {
                    nc.Data.Title = promptData.Title;
                    nc.Data.Content = response.Replace("\r", "");
                    nc.RefreshUI();

                    newNode.isCognitiveNode = true;
                    newNode.cognitiveType = type.ToString();

                    if (NodeLinkManager.Instance != null)
                    {
                        NodeLinkManager.Instance.CreateConnection(targetNode, newNode);

                        // 强制触发自动布局，确保 AI 节点完美排列，不遮挡其他节点
                        if (AutoLayoutSystem.Instance != null)
                        {
                            AutoLayoutSystem.Instance.RefreshLayout(targetNode);
                        }
                    }

                    NodeCardManager.Instance.SelectNode(newNode, false);

                    if (UserBehaviorSystem.Instance != null)
                        UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Intervention_Triggered, targetID: newNode.NodeID, info: type.ToString());
                }
            }
        }, false);
    }

    // ==========================================
    // 逻辑 1.5：导图区生成全局节点
    // ==========================================
    public void GenerateGlobalNodeIntervention(InterventionType type, bool isManual = false)
    {
        if (ProjectContextGatherer.Instance == null) return;

        string fullContext = ProjectContextGatherer.Instance.GetTreeStructureContext();
        if (string.IsNullOrWhiteSpace(fullContext))
        {
            if (ToastSystem.Instance != null) ToastSystem.Instance.Show("当前画布为空，AI 无法生成全局洞察");
            return;
        }

        if (fullContext.Length > 1500) fullContext = fullContext.Substring(0, 1500);

        var promptData = AIPromptLibrary.GetNodeInterventionPrompt(type, isGlobal: true);

        // 如果是用户主动点击的，稍微改一下指令语气
        string userIntent = isManual ? "\n用户正在主动寻求全局维度的审查与建议。" : "";
        string prompt = $"{promptData.RolePrompt}{userIntent}\n当前全图拓扑大纲：\n{fullContext}\n直接输出生成的内容，不要带任何废话或引号。";

        if (ToastSystem.Instance != null) ToastSystem.Instance.Show("AI 正在洞察全局导图...");

        LLMManager.Instance.TaskChat(prompt, (response, success) =>
        {
            if (success && !string.IsNullOrWhiteSpace(response))
            {
                Vector2 spawnPos = Vector2.zero;
                RectTransform containerRect = NodeCardManager.Instance.cardContainer.GetComponent<RectTransform>();
                Canvas parentCanvas = containerRect.GetComponentInParent<Canvas>();
                Camera uiCam = (parentCanvas != null && parentCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? parentCanvas.worldCamera : null;
                Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(containerRect, screenCenter, uiCam, out Vector2 localCenter))
                {
                    spawnPos = localCenter;
                }

                var newNode = NodeCardManager.Instance.CreateNodeAt(spawnPos);

                if (newNode is NodeController nc)
                {
                    nc.Data.Title = isManual ? $"{promptData.Title}" : promptData.Title;
                    nc.Data.Content = response.Replace("\r", "");

                    nc.Data.Title = promptData.Title;
                    nc.Data.Content = response.Replace("\r", "");
                    nc.RefreshUI();

                    newNode.isCognitiveNode = true;
                    newNode.cognitiveType = type.ToString();

                    AINodeGlowEffect glow = newNode.gameObject.AddComponent<AINodeGlowEffect>();
                    glow.StartGlow(20f);

                    if (AutoLayoutSystem.Instance != null)
                    {
                        AutoLayoutSystem.Instance.RefreshLayout(newNode);
                    }

                    NodeCardManager.Instance.SelectNode(newNode, false);

                    // =========================================================
                    // 【埋点区分】：主动 vs 被动
                    // =========================================================
                    if (UserBehaviorSystem.Instance != null)
                    {
                        if (isManual)
                        {
                            // 记录为用户的“主动功能使用”
                            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Intervention_Triggered, targetID: newNode.NodeID, info: "ManualTriggered");
                        }
                        else
                        {
                            // 记录为 AI 的“被动发呆介入” (这才会触发 Tracker 的 45 秒观察期)
                            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Intervention_Triggered, targetID: newNode.NodeID, info: type.ToString());
                        }
                    }
                }
            }
        }, false);
    }

    // ==========================================
    // 逻辑 2：成文区生成柔和提示
    // ==========================================
    private void GenerateArticleIntervention(InterventionType type, BaseNodeController contextNode = null, int stage = 1)
    {
        // 1. 检查成文区是否打开，未打开则不介入
        if (ArticleGenerator.Instance == null || !ArticleGenerator.Instance.articleModal.activeSelf) return;

        // 2. 检查聊天区是否被占用 (如果用户正在聊天框打字，不打扰)
        if (AIChatManager.Instance != null && AIChatManager.Instance.chatInput != null)
        {
            if (!string.IsNullOrWhiteSpace(AIChatManager.Instance.chatInput.text))
            {
                Debug.Log($"<color=yellow>[AI 主动介入]</color> 用户正在聊天框打字，取消本次主动引导。");
                return;
            }
        }

        // 3. 极其轻量级的调用：不再请求大模型，直接让 Copilot 中枢智能闪烁对应的按钮！
        if (CopilotActionController.Instance != null)
        {
            CopilotActionController.Instance.TriggerProactiveGlow();
        }

        // 4. 埋点记录，保留数据科学管线的完整性
        if (UserBehaviorSystem.Instance != null)
        {
            string stageInfo = stage == 0 ? "Stage0_ColdStart" : (stage == 1 ? "Macro_Global" : "Micro_Local");
            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Intervention_Triggered, targetID: "Article", info: $"{type.ToString()}_{stageInfo}");
        }
    }
}