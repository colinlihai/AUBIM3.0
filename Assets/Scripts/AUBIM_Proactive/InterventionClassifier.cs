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

    [Header("认知动作权重 (核心人设)")]
    public float Weight_Type_proactive_socratic = 0.0f;
    public float Weight_Type_proactive_counter = 0.0f;
    public float Weight_Type_proactive_global = 0.0f;
    public float Weight_Type_proactive_elaborate = 0.0f;
    public float Weight_Type_article_reflect = 0.0f;
    public float Weight_Type_article_gap = 0.0f;

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
    // 模块一：大脑管理 (读取与保存)
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

    // ==========================================
    // 模块二：快捷预设 (一键洗脑)
    // ==========================================
    [ContextMenu("注入模板：温柔导师 (Socratic)")]
    public void ApplyTemplate_Socratic()
    {
        currentBrain.Bias = 0.5f;
        currentBrain.Weight_Type_proactive_socratic = 2.0f;
        currentBrain.Weight_Type_proactive_elaborate = 1.0f;
        currentBrain.Weight_Type_proactive_counter = -3.0f;
        SaveBrain();
    }

    [ContextMenu("注入模板：杠精辩手 (Debater)")]
    public void ApplyTemplate_Debater()
    {
        currentBrain.Bias = 0.5f;
        currentBrain.Weight_Type_proactive_counter = 2.0f;
        currentBrain.Weight_Type_proactive_socratic = -3.0f;
        SaveBrain();
    }

    [ContextMenu("注入模板：恢复出厂设置 (Reset)")]
    public void ApplyTemplate_Reset()
    {
        currentBrain = new LogisticRegressionWeights();
        SaveBrain();
    }

    // ==========================================
    // 模块三：多路复用推理引擎
    // ==========================================
    public float PredictAcceptanceProbability(string interventionType, string contextArea, int canvasNodes, int selectedNodes, string lastAction = "None")
    {
        if (currentBrain == null) return 1.0f;

        float z = currentBrain.Bias;
        z += (canvasNodes / 50f) * currentBrain.Weight_CanvasNodeCount;
        z += (selectedNodes / 5f) * currentBrain.Weight_SelectedNodeCount;

        if (contextArea == "Canvas") z += currentBrain.Weight_Area_Canvas;
        else if (contextArea == "Article") z += currentBrain.Weight_Area_Article;

        if (lastAction == "Global") z += currentBrain.Weight_LastAction_Global;
        else if (lastAction == "Local") z += currentBrain.Weight_LastAction_Local;
        else if (lastAction == "Node") z += currentBrain.Weight_LastAction_Node;

        switch (interventionType)
        {
            case "proactive_socratic": z += currentBrain.Weight_Type_proactive_socratic; break;
            case "proactive_counter": z += currentBrain.Weight_Type_proactive_counter; break;
            case "proactive_global": z += currentBrain.Weight_Type_proactive_global; break;
            case "proactive_elaborate": z += currentBrain.Weight_Type_proactive_elaborate; break;
            case "article_reflect": z += currentBrain.Weight_Type_article_reflect; break;
            case "article_gap": z += currentBrain.Weight_Type_article_gap; break;
        }

        return 1.0f / (1.0f + Mathf.Exp(-z));
    }

    public bool ShouldTriggerIntervention(string interventionType, string contextArea, int canvasNodes, int selectedNodes, string lastAction = "None")
    {
        float prob = PredictAcceptanceProbability(interventionType, contextArea, canvasNodes, selectedNodes, lastAction);
        bool isApproved = prob >= acceptanceThreshold;
        bool isExploration = false;

        // 叛逆试探机制：打破信息茧房
        if (!isApproved && UnityEngine.Random.value < explorationRate)
        {
            isApproved = true;
            isExploration = true;
        }

        string color = isApproved ? (isExploration ? "yellow" : "green") : "red";
        string logMsg = $"<color={color}>[大脑裁决]</color> {interventionType} (前序动作:{lastAction}) | 预测采纳率:{(prob * 100):F1}% | 放行:{isApproved}";

        if (isExploration) logMsg += " <color=yellow><b>[触发强制试探]</b></color>";

        Debug.Log(logMsg);
        return isApproved;
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

        Debug.Log($"<color=magenta>[AUBIM Brain]</color> 触发进化！样本量：{dataset.Count}。正在进行连续奖励加权微调...");

        for (int e = 0; e < trainingEpochs; e++)
        {
            foreach (var point in dataset)
            {
                string actionStr = string.IsNullOrEmpty(point.LastArticleAction) ? "None" : point.LastArticleAction;
                float predictedProb = PredictAcceptanceProbability(point.InterventionType, point.ContextArea, point.CanvasNodeCount, point.SelectedNodeCount, actionStr);

                // 【核心算法更替】：将 RewardScore 转化为目标概率和更新权重
                float targetProb = point.RewardScore > 0 ? 1.0f : 0.0f;
                float feedbackWeight = Mathf.Abs(point.RewardScore); // 反应越强，步长越大

                // 加权梯度计算公式：Error = (Target - Prediction) * |RewardScore|
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

                switch (point.InterventionType)
                {
                    case "proactive_socratic": currentBrain.Weight_Type_proactive_socratic += learningRate * error; break;
                    case "proactive_counter": currentBrain.Weight_Type_proactive_counter += learningRate * error; break;
                    case "proactive_global": currentBrain.Weight_Type_proactive_global += learningRate * error; break;
                    case "proactive_elaborate": currentBrain.Weight_Type_proactive_elaborate += learningRate * error; break;
                    case "article_reflect": currentBrain.Weight_Type_article_reflect += learningRate * error; break;
                    case "article_gap": currentBrain.Weight_Type_article_gap += learningRate * error; break;
                }
            }
        }
        SaveBrain();
    }
}