using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class ToastSystem : MonoBehaviour
{
    public static ToastSystem Instance;

    [Header("UI 引用")]
    public GameObject toastPrefab;   // 刚才做的预制体
    public Transform toastContainer; // 刚才做的容器

    [Header("配置")]
    public float displayDuration = 2.0f; // 显示多久
    public float fadeDuration = 0.5f;    // 淡出多久

    private GameObject _currentToastObj;
    private Coroutine _currentRoutine;

    void Awake()
    {
        Instance = this;
    }

    public void Show(string message)
    {
        if (toastPrefab == null || toastContainer == null) return;

        // 1. 如果有正在跑的动画协程，立刻停止，防止逻辑冲突
        if (_currentRoutine != null)
        {
            StopCoroutine(_currentRoutine);
            _currentRoutine = null;
        }

        // 2. 如果当前有正在显示的 Toast 物体，立刻销毁
        if (_currentToastObj != null)
        {
            Destroy(_currentToastObj);
            _currentToastObj = null;
        }

        _currentToastObj = Instantiate(toastPrefab, toastContainer);

        // 4. 设置文字
        TMP_Text textComp = _currentToastObj.GetComponentInChildren<TMP_Text>();
        if (textComp != null) textComp.text = message;

        // 5. 启动新的生命周期，并记录协程
        _currentRoutine = StartCoroutine(ToastRoutine(_currentToastObj));
    }

    private IEnumerator ToastRoutine(GameObject toast)
    {
        // 确保有 CanvasGroup 用于控制透明度
        CanvasGroup cg = toast.GetComponent<CanvasGroup>();
        if (cg == null) cg = toast.AddComponent<CanvasGroup>();

        // A. 停留阶段
        yield return new WaitForSeconds(displayDuration);

        // B. 淡出阶段
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            cg.alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            yield return null;
        }

        // C. 销毁
        Destroy(toast);
    }
}