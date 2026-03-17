using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;

[Serializable]
public class LogisticRegressionWeights
{
    [Header("基础偏置 (默认倾向)")]
    public float Bias = 0.0f;

    [Header("环境特征权重")]
    public float Weight_CanvasNodeCount = 0.0f;
    public float Weight_SelectedNodeCount = 0.0f;

    [Header("空间特征权重")]
    public float Weight_Area_Canvas = 0.0f;
    public float Weight_Area_Article = 0.0f;

    [Header("画布区动作权重 (Canvas)")]
    public float Weight_Type_proactive_socratic = 0.0f;   // 反问
    public float Weight_Type_proactive_counter = 0.0f;    // 解释/反驳
    public float Weight_Type_proactive_elaborate = 0.0f;  // 追问/延伸
    public float Weight_Type_proactive_global = 0.0f;     // 全局洞察

    [Header("成文区动作权重 (Article - 4 Stage细分)")]
    public float Weight_Type_article_coldstart = 0.0f;    // 冷启动 (白纸)
    public float Weight_Type_article_expand = 0.0f;       // 顺势续写 (结尾)
    public float Weight_Type_article_stitch = 0.0f;       // 上下文缝合 (段中)
    public float Weight_Type_article_reflect = 0.0f;      // 宏观反思 (失焦)

    [Header("短期记忆权重 (上一步动作联动)")]
    public float Weight_LastAction_Global = 0.0f;
    public float Weight_LastAction_Local = 0.0f;
    public float Weight_LastAction_Node = 0.0f;
}

public class InterventionClassifier : MonoBehaviour
{
    public static InterventionClassifier Instance;

    [Header("AI 进化总控")]
    public bool isAutoEvolveEnabled = true;

    [Header("模型推理配置")]
    [Range(0f, 1f)]
    public float acceptanceThreshold = 0.4f;

    [Tooltip("即使预测用户会拒绝，AI 依然有此概率强制弹窗试探，以收集最新反馈（打破信息茧房）")]
    [Range(0f, 1f)]
    public float explorationRate = 0.05f;

    [Header("端侧训练超参数")]
    public float learningRate = 0.05f;
    public int trainingEpochs = 100;

    [Header("当前活跃大脑 (白盒可视化)")]
    public LogisticRegressionWeights currentBrain;

    private string DataFolderPath => ExperimentManager.GetUserFolderPath();
    private string ModelPath => Path.Combine(DataFolderPath, "ML_Weights.json");
    private string DatasetPath => Path.Combine(DataFolderPath, "ML_TrainingDataset.json");

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        ExperimentManager.OnExperimentStarted += InitializeBrain;
    }

    private void InitializeBrain(string subjectID)
    {
        Debug.Log("[大脑] 收到管家指令，开始读取专属权重...");
        LoadBrain();
        if (isAutoEvolveEnabled) TrainModelOnDevice();
    }

    void OnDestroy()
    {
        ExperimentManager.OnExperimentStarted -= InitializeBrain;
    }

    // ==========================================
    // 模块一 & 二：读写与预设 (保持不变)
    // ==========================================
    [ContextMenu("从本地读取大脑记忆")]
    public void LoadBrain()
    {
        if (File.Exists(ModelPath))
        {
            try { currentBrain = JsonUtility.FromJson<LogisticRegressionWeights>(File.ReadAllText(ModelPath)); }
            catch { currentBrain = new LogisticRegressionWeights(); }
        }
        else currentBrain = new LogisticRegressionWeights();
    }

    [ContextMenu("手动强制保存当前大脑状态")]
    public void SaveBrain()
    {
        if (currentBrain == null) return;
        if (!Directory.Exists(DataFolderPath)) Directory.CreateDirectory(DataFolderPath);
        File.WriteAllText(ModelPath, JsonUtility.ToJson(currentBrain, true));
        Debug.Log("<color=orange>[AUBIM Brain]</color> 大脑当前权重已永久保存！");
    }

    [ContextMenu("注入模板：恢复出厂设置 (Reset)")]
    public void ApplyTemplate_Reset()
    {
        currentBrain = new LogisticRegressionWeights();
        SaveBrain();
    }

    // ==========================================
    // 模块三：带启发式偏置的多路复用推理引擎
    // ==========================================

    /// <summary>
    /// 预测采纳概率。加入 targetContent 参数用于文本特征解析。
    /// </summary>
    public float PredictAcceptanceProbability(string interventionType, string contextArea, int canvasNodes, int selectedNodes, string lastAction = "None", string targetContent = "")
    {
        if (currentBrain == null) return 1.0f;

        // 1. 基础线性组合
        float z = currentBrain.Bias;
        z += (canvasNodes / 50f) * currentBrain.Weight_CanvasNodeCount;
        z += (selectedNodes / 5f) * currentBrain.Weight_SelectedNodeCount;

        if (contextArea == "Canvas") z += currentBrain.Weight_Area_Canvas;
        else if (contextArea == "Article") z += currentBrain.Weight_Area_Article;

        if (lastAction == "Global") z += currentBrain.Weight_LastAction_Global;
        else if (lastAction == "Local") z += currentBrain.Weight_LastAction_Local;
        else if (lastAction == "Node") z += currentBrain.Weight_LastAction_Node;

        // 2. 匹配具体类型的专属权重
        switch (interventionType)
        {
            case "proactive_socratic": z += currentBrain.Weight_Type_proactive_socratic; break;
            case "proactive_counter": z += currentBrain.Weight_Type_proactive_counter; break;
            case "proactive_elaborate": z += currentBrain.Weight_Type_proactive_elaborate; break;
            case "proactive_global": z += currentBrain.Weight_Type_proactive_global; break;

            case "article_coldstart": z += currentBrain.Weight_Type_article_coldstart; break;
            case "article_expand": z += currentBrain.Weight_Type_article_expand; break;
            case "article_stitch": z += currentBrain.Weight_Type_article_stitch; break;
            case "article_reflect": z += currentBrain.Weight_Type_article_reflect; break;
        }

        // =========================================================
        // 3. 神级注入：基于文本特征的启发式偏置 (Heuristic Bias)
        // =========================================================
        float heuristicBias = 0f;
        if (!string.IsNullOrWhiteSpace(targetContent))
        {
            // 特征 A：用户在提问或疑惑
            if (targetContent.Contains("?") || targetContent.Contains("？") || targetContent.Contains("为什么") || targetContent.Contains("怎么"))
            {
                if (interventionType == "proactive_counter") heuristicBias += 1.5f; // 极其需要解答/解释
                if (interventionType == "proactive_socratic") heuristicBias -= 2.0f; // 绝对不要在用户提问时反问他，会惹怒用户
            }
            // 特征 B：字数极其匮乏
            else if (targetContent.Length < 15)
            {
                if (interventionType == "proactive_elaborate") heuristicBias += 1.2f; // 急需延伸和补充细节
            }
        }

        // 将启发式偏置叠加到逻辑回归参数中
        z += heuristicBias;

        // 4. Sigmoid 激活函数
        return 1.0f / (1.0f + Mathf.Exp(-z));
    }

    // ==========================================
    // 模块四：端侧强化学习引擎 (连续奖励加权微调)
    // ==========================================
    [ContextMenu("强制执行一次自动进化 (Train Now)")]
    public void TrainModelOnDevice()
    {
        if (!isAutoEvolveEnabled || !File.Exists(DatasetPath)) return;

        List<MLDataPoint> dataset = new List<MLDataPoint>();
        string[] lines = File.ReadAllLines(DatasetPath);

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string cleanLine = line.TrimEnd(',');
            try { dataset.Add(JsonUtility.FromJson<MLDataPoint>(cleanLine)); } catch { }
        }

        if (dataset.Count < 5) return;

        Debug.Log($"<color=magenta>[AUBIM Brain]</color> 触发进化！样本量：{dataset.Count}。正在进行加权微调...");

        for (int e = 0; e < trainingEpochs; e++)
        {
            foreach (var point in dataset)
            {
                string actionStr = string.IsNullOrEmpty(point.LastArticleAction) ? "None" : point.LastArticleAction;

                // 训练时，我们仅拟合用户的偏好历史，不加启发式偏置干扰
                float predictedProb = PredictAcceptanceProbability(point.InterventionType, point.ContextArea, point.CanvasNodeCount, point.SelectedNodeCount, actionStr, "");

                float targetProb = point.RewardScore > 0 ? 1.0f : 0.0f;
                float feedbackWeight = Mathf.Abs(point.RewardScore);
                float error = (targetProb - predictedProb) * feedbackWeight;

                float normCanvasNodes = point.CanvasNodeCount / 50f;
                float normSelectedNodes = point.SelectedNodeCount / 5f;

                currentBrain.Bias += learningRate * error;
                currentBrain.Weight_CanvasNodeCount += learningRate * error * normCanvasNodes;
                currentBrain.Weight_SelectedNodeCount += learningRate * error * normSelectedNodes;

                if (point.ContextArea == "Canvas") currentBrain.Weight_Area_Canvas += learningRate * error;
                else if (point.ContextArea == "Article") currentBrain.Weight_Area_Article += learningRate * error;

                if (actionStr == "Global") currentBrain.Weight_LastAction_Global += learningRate * error;
                else if (actionStr == "Local") currentBrain.Weight_LastAction_Local += learningRate * error;
                else if (actionStr == "Node") currentBrain.Weight_LastAction_Node += learningRate * error;

                // 匹配 8 大细分类型进行权重更新
                switch (point.InterventionType)
                {
                    case "proactive_socratic": currentBrain.Weight_Type_proactive_socratic += learningRate * error; break;
                    case "proactive_counter": currentBrain.Weight_Type_proactive_counter += learningRate * error; break;
                    case "proactive_elaborate": currentBrain.Weight_Type_proactive_elaborate += learningRate * error; break;
                    case "proactive_global": currentBrain.Weight_Type_proactive_global += learningRate * error; break;

                    case "article_coldstart": currentBrain.Weight_Type_article_coldstart += learningRate * error; break;
                    case "article_expand": currentBrain.Weight_Type_article_expand += learningRate * error; break;
                    case "article_stitch": currentBrain.Weight_Type_article_stitch += learningRate * error; break;
                    case "article_reflect": currentBrain.Weight_Type_article_reflect += learningRate * error; break;
                }
            }
        }
        SaveBrain();
    }
}