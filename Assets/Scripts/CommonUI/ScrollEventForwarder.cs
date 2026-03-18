using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 继承 IScrollHandler 接口，专门拦截并透传滚动事件
public class ScrollEventForwarder : MonoBehaviour, IScrollHandler
{
    private ScrollRect _parentScrollRect;

    void Start()
    {
        // 向上层层寻找，直到找到管理这个气泡的 ScrollRect
        _parentScrollRect = GetComponentInParent<ScrollRect>();
    }

    public void OnScroll(PointerEventData eventData)
    {
        // 当鼠标在气泡上滚动时，如果有父级 ScrollRect，直接把事件扔给它处理！
        if (_parentScrollRect != null)
        {
            _parentScrollRect.OnScroll(eventData);
        }
    }
}