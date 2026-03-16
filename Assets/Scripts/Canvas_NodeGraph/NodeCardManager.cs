using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Linq;
using System.Collections;
using System.Text.RegularExpressions;

public class NodeCardManager : MonoBehaviour
{
    public static NodeCardManager Instance;

    [Header("Prefabs & Containers")]
    public GameObject nodeCardPrefab;
    public Transform cardContainer;
    public Transform lineLayer;

    [Header("回收站")]
    public Transform recycleBin;

    // 核心数据
    private List<BaseNodeController> _selectedNodes = new List<BaseNodeController>();
    private Dictionary<string, BaseNodeController> _nodeCardRegistry = new Dictionary<string, BaseNodeController>();

    void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        Instance = this;

        // 自动创建回收站
        if (recycleBin == null)
        {
            GameObject bin = new GameObject("RecycleBin");
            bin.transform.SetParent(transform);
            bin.SetActive(false); // 确保回收站本身也是隐藏的
            recycleBin = bin.transform;
        }
    }

    void Update()
    {
        // 全局卫语句：如果在打字，禁用所有快捷键
        if (IsEditingAnyNode()) return;

        // --- 1. 删除 (Delete / Backspace) ---
        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
        {
            if (HasSelection()) DeleteSelectedNodes();
        }

        // --- 5. [合并自 NodeEditSystem] 生成子节点 (Tab) ---
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (HasSelection()) CreateChildNode();
        }

        // --- 6. [合并自 NodeEditSystem] 生成兄弟节点 (Enter) ---
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (HasSelection()) CreateSiblingNode();
        }

        // ==========================================
        // --- 7. 空格键进入编辑模式 (Space) ---
        // ==========================================
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // 为了防止误操作，通常规定只有在单选某一个节点时，按空格才进入编辑
            if (_selectedNodes.Count == 1)
            {
                if (_selectedNodes[0] is NodeController nc)
                {
                    nc.FocusBodyInput();
                }
            }
        }

        HandleDirectionalNavigation();
    }

    /// <summary>
    /// 供 AI 调用的生成节点方法
    /// </summary>
    public BaseNodeController CreateNodeFromAI(string title, string content, string type)
    {
        // 1. 确定位置：默认在屏幕中心附近随机偏移，避免重叠
        // 如果有 CanvasPanZoomController，可以基于视野中心
        Vector2 spawnPos = new Vector2(UnityEngine.Random.Range(-50, 50), UnityEngine.Random.Range(-50, 50));

        // 如果当前有选中的节点，则生成在选中节点旁边，视觉关联性更强
        if (_selectedNodes.Count > 0 && _selectedNodes[0] != null)
        {
            spawnPos = _selectedNodes[0].transform.localPosition + new Vector3(UnityEngine.Random.Range(50, 150), UnityEngine.Random.Range(-50, 50), 0);
        }

        // 2. 调用现有的 CreateNodeAt (这样可以复用命令模式，支持 Ctrl+Z 撤销)
        BaseNodeController newCtrl = CreateNodeAt(spawnPos);

        // 3. 填充数据
        if (newCtrl != null)
        {
            // 尝试转型为 NodeController (假设这是你实际的业务类，包含 Data)
            if (newCtrl is NodeController nc)
            {
                // 确保 Data 不为空 (根据你的代码风格，可能需要 new 或者已经在 Awake 里 new 了)
                if (nc.Data == null) nc.Data = new NodeData(CardType.NodeCard);

                nc.Data.Title = title;
                nc.Data.Content = content;

                // 如果有类型设置逻辑
                // if (type == "structure") nc.cardType = CardType.Structure; 

                // 强制刷新 UI (如果 NodeController 没有自动检测 Data 变化)
                // nc.RefreshView(); 
            }

            // 自动选中新生成的节点，给予用户反馈
            SelectNode(newCtrl, false);

            Debug.Log($"[AI] 已创建卡片: {title}");
        }

        return newCtrl;
    }

    // ==========================================
    // 数据结构：用于承载多层级的解析数据
    // ==========================================
    public class ExtractedPoint
    {
        public string MainPoint;
        public System.Collections.Generic.List<string> SubPoints = new System.Collections.Generic.List<string>();
    }

    // ==========================================
    // 接收清洗后的树状数据，负责物理生成节点
    // ==========================================
    public void BuildTreeFromAIData(AITreeRootData rootData, Vector2 targetLocalPosition)
    {
        if (rootData == null) return;

        int totalNodesCreated = 0;

        // 1. 生成总述节点 (Root)
        var parentNode = CreateNodeAtPosition(targetLocalPosition);
        if (parentNode is NodeController nc)
        {
            nc.Data.Title = rootData.rootTitle ?? "核心观点";
            nc.Data.Content = rootData.rootContent ?? "";
            nc.RefreshUI();
            parentNode.isCognitiveNode = false;
            parentNode.cognitiveType = "chat_extraction_root";
        }
        totalNodesCreated++;

        // 2. 递归生成子节点
        if (rootData.children != null && rootData.children.Count > 0)
        {
            // 给递归函数一个起始的 Y 轴偏移，避免叠在一起
            float startYOffset = -150f;
            foreach (var childData in rootData.children)
            {
                totalNodesCreated += SpawnNodeRecursive(childData, parentNode, targetLocalPosition, ref startYOffset, 1);
            }
        }

        // [埋点记录]
        if (UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(BehaviorEventType.AI_Chat_Response, targetID: "Chat", info: "Extracted_To_Canvas_AI", value: totalNodesCreated);
        }

        // 3. 延迟排版
        StartCoroutine(DelayedLayoutAndSelect(parentNode, totalNodesCreated));
    }

    // 递归生成器：支持无限深度的子节点生成
    private int SpawnNodeRecursive(AITreeNodeData data, BaseNodeController parentNode, Vector2 basePos, ref float currentYOffset, int depth)
    {
        int nodesMade = 0;

        // X 轴根据深度偏移，Y 轴不断累加往下移
        Vector2 spawnPos = basePos + new Vector2(300 * depth, currentYOffset);
        var node = CreateNodeAtPosition(spawnPos);

        if (node is NodeController nc)
        {
            nc.Data.Title = data.title ?? "细节";
            nc.Data.Content = data.content ?? "";
            nc.RefreshUI();
            node.isCognitiveNode = false;
            node.cognitiveType = $"chat_extraction_l{depth}";
        }

        // 连线
        if (NodeLinkManager.Instance != null)
        {
            NodeLinkManager.Instance.CreateConnection(parentNode, node);
        }
        nodesMade++;
        currentYOffset -= 120f; // 移动 Y 游标

        // 递归挖掘更深层
        if (data.children != null && data.children.Count > 0)
        {
            foreach (var childData in data.children)
            {
                nodesMade += SpawnNodeRecursive(childData, node, basePos, ref currentYOffset, depth + 1);
            }
        }

        return nodesMade;
    }

    // ==========================================
    // 延迟排版协程
    // ==========================================
    private System.Collections.IEnumerator DelayedLayoutAndSelect(BaseNodeController parentNode, int childCount)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        if (childCount > 0 && AutoLayoutSystem.Instance != null)
        {
            AutoLayoutSystem.Instance.RefreshLayout(parentNode);
        }

        SelectNode(parentNode, false);
    }

    // =========================================================
    // 方向键导航逻辑
    // =========================================================
    private void HandleDirectionalNavigation()
    {
        // 只有在单选模式下才启用导航，避免多选时逻辑混乱
        if (_selectedNodes.Count != 1) return;

        BaseNodeController current = _selectedNodes[0];
        if (current == null) return;

        // 左箭头 -> 找爸爸
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            NavigateToParent(current);
        }
        // 右箭头 -> 找大儿子
        else if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            NavigateToChild(current);
        }
        // 上箭头 -> 找哥哥
        else if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            NavigateToSibling(current, -1); // -1 代表向前
        }
        // 下箭头 -> 找弟弟
        else if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            NavigateToSibling(current, 1);  // 1 代表向后
        }
    }

    private void NavigateToParent(BaseNodeController current)
    {
        if (current.parentNode != null)
        {
            // 切换选中并聚焦
            SelectNode(current.parentNode, false);
        }
    }

    private void NavigateToChild(BaseNodeController current)
    {
        if (current.childNodes != null && current.childNodes.Count > 0)
        {
            // 默认选中第一个子节点
            // 优化：如果有逻辑顺序（比如按Y轴排序），可以选最中间那个，这里简单选第一个
            BaseNodeController firstChild = current.childNodes[0];
            SelectNode(firstChild, false);
        }
    }

    private void NavigateToSibling(BaseNodeController current, int offset)
    {
        // 1. 获取同级列表
        List<BaseNodeController> siblings = null;

        if (current.parentNode != null)
        {
            // 有爸爸，找爸爸的孩子们
            siblings = current.parentNode.childNodes;
        }
        else
        {
            // 没爸爸 (根节点)，找所有根节点
            // 注意：这可能比较慢，如果根节点很多。
            siblings = _nodeCardRegistry.Values.Where(n => n.parentNode == null).ToList();
        }

        if (siblings == null || siblings.Count <= 1) return;

        // 2. 排序 (视觉顺序)
        // 默认 siblings 列表可能是乱序的（取决于连接顺序），我们需要按 Y 轴排序以符合上下键直觉
        // OrderByDescending: Y轴越大越在上面
        siblings.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));

        // 3. 找到自己在排序后列表中的位置
        int myIndex = siblings.IndexOf(current);
        if (myIndex == -1) return;

        // 4. 计算目标索引
        int targetIndex = myIndex + offset;

        // 5. 范围检查
        if (targetIndex >= 0 && targetIndex < siblings.Count)
        {
            BaseNodeController target = siblings[targetIndex];
            SelectNode(target, false);
        }
    }

    private bool IsEditingAnyNode()
    {
        var currentObj = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
        return currentObj != null && currentObj.GetComponent<TMPro.TMP_InputField>() != null;
    }

    // =========================================================
    //  生成逻辑 (合并自 NodeEditSystem)
    // =========================================================

    private void CreateChildNode()
    {
        BaseNodeController parent = GetFirstSelected();
        if (parent == null || parent.cardType != CardType.NodeCard) return;

        // 1. 确定位置：生成在爸爸的位置 (Layout 会马上把它移开)
        Vector2 spawnPos = parent.transform.localPosition;

        // 2. 生成新节点
        BaseNodeController child = CreateNodeAt(spawnPos);

        if (child != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(child.GetComponent<RectTransform>());
            // 3. 建立连接
            if (NodeLinkManager.Instance != null)
            {
                NodeLinkManager.Instance.CreateConnection(parent, child);
            }
            if (AutoLayoutSystem.Instance != null)
            {
                AutoLayoutSystem.Instance.RefreshLayout(parent);
            }

            // 4. 选中并打字
            FocusNewNode(child);
        }
    }

    public BaseNodeController CreateNodeAtPosition(Vector2 pos)
    {
        // 1. 实例化
        GameObject newObj = Instantiate(nodeCardPrefab, cardContainer);
        BaseNodeController ctrl = newObj.GetComponent<BaseNodeController>();

        // 2. 初始化数据
        ctrl.Data = new NodeData(CardType.NodeCard);

        // 3. 设置位置
        RectTransform rt = newObj.GetComponent<RectTransform>();
        rt.anchoredPosition = pos;

        // 4. 注册并记录命令 (支持撤销)
        // 这一步很重要，我们手动构造 Command 并执行
        var cmd = new CreateNodeCommand(ctrl);
        CommandManager.Instance.ExecuteCommand(cmd);

        return ctrl;
    }

    private void CreateSiblingNode()
    {
        BaseNodeController current = GetFirstSelected();
        if (current == null || current.cardType != CardType.NodeCard) return;

        BaseNodeController parent = current.parentNode;
        Vector2 spawnPos = current.transform.localPosition;

        BaseNodeController sibling = CreateNodeAt(spawnPos);

        if (sibling != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(sibling.GetComponent<RectTransform>());
            int targetSiblingIndex = current.transform.GetSiblingIndex() + 1;
            sibling.transform.SetSiblingIndex(targetSiblingIndex);

            if (parent != null)
            {
                // 有爸爸，连同一个爸爸
                if (NodeLinkManager.Instance != null)
                {
                    NodeLinkManager.Instance.CreateConnection(parent, sibling);
                    NodeLinkManager.Instance.ReorderChildren(parent);
                }
                if (AutoLayoutSystem.Instance != null)
                {
                    AutoLayoutSystem.Instance.RefreshLayout(parent);
                }
            }
            else
            {
                // 没爸爸 (根节点)，只是在旁边生成一个新的根节点
                sibling.transform.localPosition = spawnPos + new Vector2(0, -100f);
            }
            FocusNewNode(sibling); 
        }
    }

    private void FocusNewNode(BaseNodeController node)
    {
        SelectNode(node, false);
        if (node is NodeController nc)
        {
            nc.FocusBodyInput();
        }
    }

    // =========================================================
    //  基础管理功能
    // =========================================================

    public void CreateNodeCard(Vector2 screenPosition, Camera renderCamera = null)
    {
        GameObject newCardObj = Instantiate(nodeCardPrefab, cardContainer);
        RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
        LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);

        float offsetX = cardRect.rect.width * 0.5f;
        float offsetY = cardRect.rect.height * 0.5f;

        RectTransform containerRect = cardContainer.GetComponent<RectTransform>();

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            containerRect,
            screenPosition,
            renderCamera,
            out Vector2 localPoint))
        {
            newCardObj.transform.localPosition = new Vector2(localPoint.x - offsetX, localPoint.y + offsetY);
        }

        BaseNodeController controller = newCardObj.GetComponent<BaseNodeController>();
        if (controller != null)
        {
            // 注册到命令系统，使其可以被 Ctrl+Z 撤销
            if (CommandManager.Instance != null)
            {
                var cmd = new CreateNodeCommand(controller);
                CommandManager.Instance.ExecuteCommand(cmd);
            }
            SelectNode(controller, false);
            if (controller is NodeController nc)
            {
                nc.FocusBodyInput();
            }
        }
    }

    // [供内部和外部调用的生成接口]
    public BaseNodeController CreateNodeAt(Vector2 localPosition)
    {
        GameObject newCardObj = Instantiate(nodeCardPrefab, cardContainer);
        newCardObj.transform.localPosition = localPosition;

        RectTransform cardRect = newCardObj.GetComponent<RectTransform>();
        LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);

        BaseNodeController controller = newCardObj.GetComponent<BaseNodeController>();

        if (controller != null && CommandManager.Instance != null)
        {
            var cmd = new CreateNodeCommand(controller);
            CommandManager.Instance.ExecuteCommand(cmd);
        }

        return controller;
    }

    public void DeleteSelectedNodes()
    {
        int count = _selectedNodes.Count;

        var nodesToDelete = new List<BaseNodeController>(_selectedNodes);
        if (CommandManager.Instance != null)
        {
            foreach (var node in nodesToDelete)
            {
                if (node == null || !node.gameObject.activeSelf) continue;

                // 构建命令 (在命令内部执行软删除)
                var cmd = new DeleteNodeCommand(node);
                CommandManager.Instance.ExecuteCommand(cmd);
            }
        }
        else
        {
            foreach (var node in nodesToDelete)
            {
                if (node == null) continue;
                node.SetSelected(false);
                UnregisterNodeCard(node.NodeID);
                Destroy(node.gameObject);
            }
        }
        // 3. 清空选中状态
        _selectedNodes.Clear();
    }

    // =========================================================
    //  选中与注册逻辑
    // =========================================================

    public bool HasSelection() => _selectedNodes.Count > 0;

    public void SelectNode(BaseNodeController node, bool isMultiSelect)
    {
        if (!isMultiSelect) DeselectAll();

        if (isMultiSelect && _selectedNodes.Contains(node))
        {
            _selectedNodes.Remove(node);
            node.SetSelected(false);
        }
        else
        {
            if (!_selectedNodes.Contains(node)) _selectedNodes.Add(node);
            node.SetSelected(true);
        }

        // 只要用户切换了焦点（证明用户在主动推进任务），立刻打断可能正在发呆闪烁的 AI 按钮
        InterventionTracker.Instance.AbortLocalBreathing();

        // [埋点] 切换焦点
        if (node != null && UserBehaviorSystem.Instance != null)
        {
            UserBehaviorSystem.Instance.LogEvent(
                BehaviorEventType.Selection_Change,
                targetID: node.NodeID,
                info: node.cardType.ToString(),
                value: 1
            );
        }
    }

    public void DeselectAll()
    {
        foreach (var node in _selectedNodes)
        {
            if (node != null)
            {
                node.SetSelected(false);
            }
        }
        _selectedNodes.Clear();
    }

    public void FocusNode(string id)
    {
        if (_nodeCardRegistry.ContainsKey(id))
        {
            var targetNode = _nodeCardRegistry[id];
            if (targetNode != null && CanvasPanZoomController.Instance != null)
            {
                Vector2 localPos = targetNode.transform.localPosition;
                CanvasPanZoomController.Instance.FocusOn(localPos);
                SelectNode(targetNode, false);
            }
        }
    }

    public BaseNodeController GetFirstSelected()
    {
        return _selectedNodes.Count > 0 ? _selectedNodes[0] : null;
    }

    public List<BaseNodeController> GetSelectedNodes()
    {
        return new List<BaseNodeController>(_selectedNodes);
    }

    public List<BaseNodeController> GetAllNodes() => _nodeCardRegistry.Values.ToList();

    public bool IsDescendant(BaseNodeController node, BaseNodeController potentialAncestor)
    {
        var current = node.parentNode;
        while (current != null)
        {
            if (current == potentialAncestor) return true;
            current = current.parentNode;
        }
        return false;
    }

    public void RegisterNodeCard(string id, BaseNodeController node)
    {
        if (!string.IsNullOrEmpty(id) && !_nodeCardRegistry.ContainsKey(id))
            _nodeCardRegistry.Add(id, node);
    }

    public void UnregisterNodeCard(string id)
    {
        if (_nodeCardRegistry.ContainsKey(id)) _nodeCardRegistry.Remove(id);
    }

    public void ClearRegistries()
    {
        _nodeCardRegistry.Clear();
        _selectedNodes.Clear();
    }

    public void CancelSelection()
    {
        // 遍历当前选中的，把高亮关掉
        foreach (var node in _selectedNodes)
        {
            if (node != null) node.SetSelected(false);
        }
        _selectedNodes.Clear();
    }
}