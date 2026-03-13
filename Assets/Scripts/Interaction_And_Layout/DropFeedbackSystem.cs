using UnityEngine;
using UnityEngine.UI;

public class DropFeedbackSystem : MonoBehaviour
{
    public static DropFeedbackSystem Instance;

    public RectTransform insertLine;

    private void Awake()
    {
        Instance = this;
        HideAll();
    }

    public void HideAll()
    {
        if (insertLine) insertLine.gameObject.SetActive(false);
    }

    // --- 模式 B: 插入排序 (显示在两个节点中间) ---
    public void ShowInsertion(Vector3 worldPosition, float width)
    {
        if (insertLine == null) return;

        insertLine.gameObject.SetActive(true);

        insertLine.position = worldPosition;

        // 这里 width 通常传入的是 targetRect.rect.width (原始宽度)
        // 我们也简单做一个缩放补偿，但因为没有传入 targetRect，我们假设它应该跟随 Canvas 的缩放
        // 或者简单地保持 localScale = 1，让它随 Canvas 整体缩放 (通常线条不需要像卡片那样严格匹配 lossyScale)
        // 但为了防止线条在 Canvas 缩放时变得过细或过粗，我们可以重置一下 localScale
        insertLine.localScale = Vector3.one;

        insertLine.sizeDelta = new Vector2(width, 4f); // 线的高度固定
        insertLine.SetAsLastSibling();
    }
}