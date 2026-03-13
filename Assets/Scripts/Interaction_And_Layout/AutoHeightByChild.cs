using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways] // 允许在编辑模式下也就生效，方便预览
public class AutoHeightByChild : MonoBehaviour
{
    [Header("要跟随哪个子物体的高度")]
    public RectTransform targetChild;

    private LayoutElement _myLayoutElement;

    void OnEnable()
    {
        _myLayoutElement = GetComponent<LayoutElement>();
        UpdateHeight();
    }

    void Update()
    {
        UpdateHeight();
    }
    public void UpdateHeight()
    {
        if (targetChild != null && _myLayoutElement != null)
        {
            float childHeight = targetChild.rect.height;
            // 加一个极小的阈值，防止浮点数微小抖动导致 Layout 脏标记
            if (Mathf.Abs(_myLayoutElement.preferredHeight - childHeight) > 0.01f)
            {
                _myLayoutElement.preferredHeight = childHeight;
            }
        }
    }
}