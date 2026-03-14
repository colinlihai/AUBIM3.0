using UnityEngine;
using System;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public enum CardType
{
    NodeCard,   // 画布上的自由节点
}

public class BaseNodeController : MonoBehaviour, IPointerClickHandler
{
    [Header("身份认证")]
    public CardType cardType; // 在 Prefab 里手动选好
    public NodeData Data;

    public TMP_InputField contentInput;

    public CoreBodyVisual visual;
    public DragInputRouter dragRouter;

    public BaseNodeController parentNode;
    public List<BaseNodeController> childNodes = new List<BaseNodeController>();

    protected AutoHeightByChild _autoHeight;
    private string _tempStartText;

    // [新增] 用于标记这个节点是否是 AI 主动生成的认知支持卡片
    public bool isCognitiveNode = false;
    public string cognitiveType = ""; // 记录是 "socratic" 还是 "counter"

    public string NodeID
    {
        get
        {
            return Data != null ? Data.ID : "Uninitialized";
        }
    }

    private Transform GetLineContainer()
    {
        if (NodeCardManager.Instance != null && NodeCardManager.Instance.lineLayer != null)
        {
            return NodeCardManager.Instance.lineLayer;
        }
        return transform.parent; // 降级方案：直接画在同一层
    }

    protected virtual void Awake()
    {
        if (visual == null) visual = GetComponentInChildren<CoreBodyVisual>();
        if (dragRouter == null) dragRouter = GetComponentInChildren<DragInputRouter>();

        _autoHeight = GetComponent<AutoHeightByChild>();
        if (_autoHeight != null)
        {
            _autoHeight.UpdateHeight();
            _autoHeight.enabled = false;
        }
        if (Data == null || string.IsNullOrEmpty(Data.ID))
        {
            Data = new NodeData(cardType);
        }
        if (Data != null)
        {
            Data.OnContentChanged += HandleDataUpdate;
        }
        if (contentInput != null)
        {
            // 先移除监听，防止重复添加
            contentInput.onValueChanged.RemoveAllListeners();
            contentInput.onValueChanged.AddListener(OnInputChanged);
        }

        // 初始化显示
        RefreshUI();
    }

    protected virtual void Start()
    {
        if (visual != null)
        {
            // 当 Visual 监测到点击请求时，执行 HandleRequestSelection
            visual.OnRequestSelection += HandleRequestSelection;
        }

        if (contentInput != null)
        {
            // 1. 当用户点进去准备打字时 -> 醒醒，该干活了！
            contentInput.onSelect.AddListener((data) => {
                _tempStartText = contentInput.text;
                if (_autoHeight != null) _autoHeight.enabled = true;
            });

            // 2. 当用户点别处结束编辑时 -> 算完最后一次，去睡觉吧
            contentInput.onDeselect.AddListener((data) => {
                string finalText = contentInput.text;
                if (CommandManager.Instance != null && finalText != _tempStartText)
                {
                    var cmd = new EditContentCommand(this, _tempStartText, finalText);
                    CommandManager.Instance.ExecuteCommand(cmd);
                }

                if (_autoHeight != null)
                {
                    _autoHeight.UpdateHeight(); // 确保最后状态正确
                    _autoHeight.enabled = false;
                }

                if (cardType == CardType.NodeCard && AutoLayoutSystem.Instance != null)
                {
                    AutoLayoutSystem.Instance.RefreshLayout(this);
                }
            });

            // 3. (保险) 当代码修改了文本 (比如 AI 生成 Summary，或者数据同步)
            // 这种时候不会触发 onSelect，所以我们要手动刷新一次
            contentInput.onValueChanged.AddListener((val) => {
                // 如果当前没有在编辑 (enabled == false)，说明是代码改的
                // 或者是粘贴进去的，手动刷一帧
                if (_autoHeight != null && !_autoHeight.enabled)
                {
                    _autoHeight.UpdateHeight();
                }
                // 原有的通知父级逻辑
                NotifyParentUpdate();
            });
        }

        if (NodeCardManager.Instance != null)
        {
            if (cardType == CardType.NodeCard)
            {
                // 我是本体，注册到 NodeRegistry
                NodeCardManager.Instance.RegisterNodeCard(NodeID, this);
            }
        }
    }

    protected virtual void OnDestroy()
    {
        // 5. 销毁时取消订阅，防止内存泄漏
        if (visual != null) visual.OnRequestSelection -= HandleRequestSelection;
        if (Data != null) Data.OnContentChanged -= HandleDataUpdate;
        if (contentInput != null) contentInput.onValueChanged.RemoveListener(OnInputChanged);
        if (NodeCardManager.Instance != null && cardType == CardType.NodeCard)
        {
            NodeCardManager.Instance.UnregisterNodeCard(NodeID);
        }

        if (cardType == CardType.NodeCard)
        {
            if (NodeLinkManager.Instance != null)
            {
                NodeLinkManager.Instance.DeleteNodeCleanup(this.NodeID);
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 防止拖拽结束时误触点击
        if (eventData.dragging) return;

        bool isMulti = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // --- 双击检测 (Count == 2) ---
        if (eventData.clickCount == 2)
        {
            // 只有 LeafCard/GroupCard 双击才跳转找 NodeCard
            // (NodeCard 双击可以做别的，比如进入编辑，或者这里暂不处理)
            if (cardType != CardType.NodeCard)
            {
                if (NodeCardManager.Instance != null)
                {
                    // 呼叫 Manager 进行聚焦
                    NodeCardManager.Instance.FocusNode(NodeID);
                }
            }
        }
        // --- 单击检测 ---
        else if (eventData.clickCount == 1)
        {
            // 走通用的选中逻辑 (包含联动高亮)
            if (NodeCardManager.Instance != null)
            {
                NodeCardManager.Instance.SelectNode(this, isMulti);
            }
        }
    }

    /// <summary>
    /// 处理来自 Visual 的“请求选中”事件
    /// </summary>
    protected virtual void HandleRequestSelection()
    {
        bool isMulti = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // B. 呼叫 Manager 进行裁决
        if (NodeCardManager.Instance != null)
        {
            NodeCardManager.Instance.SelectNode(this, isMulti);
        }
        else
        {
            SetSelected(true);
        }
    }

    public virtual void SetSelected(bool isSelected)
    {
        // 将状态传递给 Visual 层去更新 Outline 颜色
        if (visual != null)
        {
            visual.SetSelected(isSelected);
        }
    }

    public void SetData(NodeData sharedData)
    {
        if (this.Data != null)
        {
            this.Data.OnContentChanged -= HandleDataUpdate;
        }
        this.Data = sharedData;
        if (this.Data != null)
        {
            this.Data.OnContentChanged += HandleDataUpdate;

            // 立即刷新一次显示
            HandleDataUpdate(this.Data.Content);
        }
    }

    private void OnInputChanged(string val)
    {
        if (Data != null)
        {
            Data.Content = val;
            NotifyParentUpdate();
        }
    }

    private void HandleDataUpdate(string newContent)
    {
        if (contentInput != null && contentInput.text != newContent)
        {
            contentInput.SetTextWithoutNotify(newContent);
            if (_autoHeight != null) _autoHeight.UpdateHeight();
        }
        NotifyParentUpdate();
    }

    public virtual void RefreshUI()
    {
        if (Data != null)
        {
            HandleDataUpdate(Data.Content);
        }
    }

    protected virtual void NotifyParentUpdate()
    {
        //if (transform.parent != null)
        //{
        //    var parentGroup = transform.parent.GetComponentInParent<GroupController>();

        //    if (parentGroup != null)
        //    {
        //        parentGroup.RequestUpdateSummary();
        //    }
        //}
    }

    public void SetGhostMode(bool isGhost)
    {
        // 1. 设置自己
        if (visual != null)
        {
            visual.SetGhostMode(isGhost);
        }

        // 2. 递归设置所有子节点
        // 注意：这里假设 childNodes 已经包含了所有层级的直接子节点
        // 递归调用会让子节点的子节点也变色
        if (childNodes != null)
        {
            foreach (var child in childNodes)
            {
                if (child != null)
                {
                    child.SetGhostMode(isGhost);
                }
            }
        }
    }

    // ==========================================
    // 【新增】：将 AI 草稿节点实体化为正式节点
    // ==========================================
    public void SolidifyCognitiveNode()
    {
        if (isCognitiveNode)
        {
            isCognitiveNode = false; // 摘掉 AI 标签
            cognitiveType = "";      // 清空类型

            // 触发视觉刷新，使其恢复 1.0 的完全不透明状态
            if (visual != null)
            {
                // 利用现有的交互状态触发一次刷新
                visual.SetSelected(false);
                visual.SetSelected(true);
            }

            Debug.Log($"<color=green>[Node]</color> 节点 {NodeID} 被用户操作实体化！");
        }
    }
}
