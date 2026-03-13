using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ResizeHandle : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler
{
    [Header("控制哪个物体的宽度")]
    public LayoutElement targetLayoutElement; // 拖入 CoreBody

    [Header("最小宽度")]
    public float minWidth = 100f;

    private float _initialWidth;
    private float _initialMouseX;
    private float _startWidth;

    private BaseNodeController _controller;

    private AutoWidthByContent _autoWidthComp;
    private bool _wasAutoBeforeDrag;

    private void Start()
    {
        _controller = GetComponentInParent<BaseNodeController>();
        if (targetLayoutElement != null)
            _autoWidthComp = targetLayoutElement.GetComponent<AutoWidthByContent>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (targetLayoutElement == null) return;
        _startWidth = targetLayoutElement.preferredWidth;
        _initialWidth = targetLayoutElement.preferredWidth;
        _initialMouseX = eventData.position.x;

        if (_autoWidthComp != null)
        {
            _wasAutoBeforeDrag = _autoWidthComp.isAutoWidth;
            _autoWidthComp.isAutoWidth = false;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (targetLayoutElement == null) return;

        // 计算鼠标移动的水平距离
        float deltaX = eventData.position.x - _initialMouseX;

        // 新宽度 = 初始宽度 + 移动距离
        // (注意：这里假设把手在右边，往右拖是增加。如果在左边逻辑要反过来)
        float newWidth = Mathf.Max(minWidth, _initialWidth + deltaX);

        // 修改 LayoutElement 的 PreferredWidth，LayoutGroup 会自动重新排版
        targetLayoutElement.preferredWidth = newWidth;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_controller != null && targetLayoutElement != null)
        {
            // [新增] 记录终点
            float endWidth = targetLayoutElement.preferredWidth;

            // 只有宽度真的变了，才提交命令 (防止手滑微动)
            if (Mathf.Abs(endWidth - _startWidth) > 1f)
            {
                // 构建命令
                var cmd = new ResizeNodeCommand(_controller, targetLayoutElement, _startWidth, endWidth, _wasAutoBeforeDrag);
                if (CommandManager.Instance != null)
                {
                    CommandManager.Instance.ExecuteCommand(cmd);
                }
            }
            else
            {
                // [新增] 如果没怎么动(属于误触)，恢复原状
                // 比如用户点了一下把手但没拖，这时候应该把自动模式设回去
                if (_autoWidthComp != null) _autoWidthComp.isAutoWidth = _wasAutoBeforeDrag;
            }

            if (_controller.cardType == CardType.NodeCard && AutoLayoutSystem.Instance != null)
            {
                AutoLayoutSystem.Instance.RefreshLayout(_controller);
            }
        }
    }
}