using UnityEngine;
using UnityEngine.UI;

public class ConnectionLine : MonoBehaviour
{
    public RectTransform startNode; // 父节点
    public RectTransform endNode;   // 子节点

    public float lineThickness = 5f;

    private Image _h1;
    private Image _v;
    private Image _h2;

    public void Initialize(Transform parentContainer)
    {
        // 设置自身父物体 (通常是 LineContainer，位于卡片层下方)
        transform.SetParent(parentContainer);
        transform.localScale = Vector3.one;
        transform.localPosition = Vector3.zero;

        // 创建三段 Image
        _h1 = CreateSegment("H1");
        _v = CreateSegment("V");
        _h2 = CreateSegment("H2");
    }

    private Image CreateSegment(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.localScale = Vector3.one;

        Image img = go.AddComponent<Image>();
        var theme = ThemeManager.Instance != null ? ThemeManager.Instance.currentTheme : null;
        img.color = (theme != null) ? theme.lineColor : Color.gray;
        img.raycastTarget = false; // 关键：不阻挡鼠标点击

        // 设置锚点为左上角，方便计算
        RectTransform rt = img.rectTransform;
        rt.pivot = new Vector2(0, 0.5f); // 旋转中心在左侧中点
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;

        return img;
    }

    public void UpdateGeometry()
    {
        if (startNode == null || endNode == null) return;
        Vector3 startPos = GetEdgeCenter(startNode, true);  // true = 右边
        Vector3 endPos = GetEdgeCenter(endNode, false);     // false = 左边

        startPos = transform.InverseTransformPoint(startPos);
        endPos = transform.InverseTransformPoint(endPos);

        // 3. 强制 Z 轴归零 (防止 UI 层级导致的 3D 偏移)
        startPos.z = 0;
        endPos.z = 0;

        // 4. 计算三段线 (保持原有逻辑)
        float midX = (startPos.x + endPos.x) / 2f;


        SetSegment(_h1, startPos, new Vector3(midX, startPos.y, 0));
        SetSegment(_v, new Vector3(midX, startPos.y, 0), new Vector3(midX, endPos.y, 0), true);
        SetSegment(_h2, new Vector3(midX, endPos.y, 0), endPos);
    }

    private Vector3 GetEdgeCenter(RectTransform rt, bool isRightSide)
    {
        // 1. 获取 RectTransform 在局部空间中的四个角 (Local Space)
        // rect.xMin 是左边, rect.xMax 是右边, rect.center.y 是垂直中心
        float x = isRightSide ? rt.rect.xMax : rt.rect.xMin;
        float y = rt.rect.center.y;

        // 2. 组装局部坐标点
        Vector3 localPoint = new Vector3(x, y, 0);

        // 3. 转为世界坐标 (World Space)
        return rt.TransformPoint(localPoint);
    }

    private void SetSegment(Image segment, Vector3 p1, Vector3 p2, bool isVertical = false)
    {
        RectTransform rt = segment.rectTransform;
        float dist = Vector3.Distance(p1, p2);

        // 避免长度为 0 导致的闪烁
        if (dist < 1f) dist = 0f;

        if (isVertical)
        {
            float midY = (p1.y + p2.y) / 2f;

            // 2. X 轴位置：因为 Pivot.x 是 0 (左边缘)，为了让线居中显示，
            // 我们需要向左偏移半个线宽
            float xPos = p1.x - lineThickness * 0.5f;

            rt.localPosition = new Vector3(xPos, midY, 0);
            rt.sizeDelta = new Vector2(lineThickness, dist);
        }
        else
        {
            // 水平线
            // 位置设为 X 轴较小的那个点
            float minX = Mathf.Min(p1.x, p2.x);
            rt.localPosition = new Vector3(minX, p1.y, 0);
            rt.sizeDelta = new Vector2(dist, lineThickness);
        }
        rt.localRotation = Quaternion.identity;
    }
}
