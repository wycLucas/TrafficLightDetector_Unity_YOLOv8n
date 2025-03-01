using UnityEngine;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;

/// <summary>
/// WebSocket视频流客户端
/// 功能：建立WebSocket连接、传输视频帧、接收并可视化检测结果
/// </summary>
public class WebSocketClient : MonoBehaviour
{
    private ClientWebSocket webSocket;          // WebSocket客户端实例
    public VideoPlayer videoPlayer;             // 视频播放组件
    public RawImage rawImage;                   // 视频渲染画布
    public int videoWidth = 640;                // 视频采集宽度
    public int videoHeight = 640;               // 视频采集高度

    IEnumerator Start()
    {
        Debug.Log("[系统初始化] WebSocket客户端启动");
        webSocket = new ClientWebSocket();
        ConnectWebSocket();

        // 等待WebSocket连接建立
        while (webSocket.State != WebSocketState.Open)
        {
            yield return null; // 每帧等待连接状态更新
        }

        videoPlayer.Play();

        // 等待视频流开始播放
        while (!videoPlayer.isPlaying)
        {
            yield return null;
        }

        // 启动核心业务协程
        StartCoroutine(SendFrames());
        StartCoroutine(ReceiveLoop());
    }

    /// <summary>
    /// 建立WebSocket连接
    /// </summary>
    private async void ConnectWebSocket()
    {
        try
        {
            await webSocket.ConnectAsync(new Uri("ws://localhost:8700"), CancellationToken.None);
            Debug.Log("[连接状态] WebSocket连接成功");
        }
        catch (Exception e)
        {
            Debug.LogError($"[连接错误] 连接异常：{e.Message}（请检查服务器状态）");
        }
    }

    /// <summary>
    /// 视频帧发送协程
    /// 功能：定时捕获视频帧并编码传输
    /// </summary>
    System.Collections.IEnumerator SendFrames()
    {
        while (videoPlayer.isPlaying)
        {
            // 捕获当前视频帧
            Texture2D frame = new Texture2D(videoWidth, videoHeight, TextureFormat.RGB24, false);
            RenderTexture.active = videoPlayer.targetTexture;
            frame.ReadPixels(new Rect(0, 0, videoWidth, videoHeight), 0, 0);
            frame.Apply();
            
            // 编码为JPEG格式
            byte[] frameBytes = frame.EncodeToJPG();
            Debug.Log($"[数据传输] 捕获视频帧，尺寸：{frameBytes.Length}字节");
            
            SendFrameAsync(frameBytes);
            yield return new WaitForSeconds(1f);  // 控制传输频率（1秒/帧）
        }
        Debug.Log("[状态变更] 视频播放终止，停止帧采集");
    }

    /// <summary>
    /// 异步发送视频帧数据
    /// </summary>
    private async void SendFrameAsync(byte[] frameBytes)
    {
        if (webSocket.State == WebSocketState.Open)
        {
            ArraySegment<byte> bytesToSend = new ArraySegment<byte>(frameBytes);
            await webSocket.SendAsync(bytesToSend, WebSocketMessageType.Binary, true, CancellationToken.None);
        }
        else
        {
            Debug.LogWarning("[连接警告] 连接未就绪，放弃帧传输");
        }
    }

    /// <summary>
    /// 数据接收协程
    /// 功能：持续监听并处理服务器响应
    /// </summary>
    private IEnumerator ReceiveLoop()
    {
        while (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            var buffer = new byte[1024];
            var receiveTask = webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            
            yield return new WaitUntil(() => receiveTask.IsCompleted);
            var result = receiveTask.GetAwaiter().GetResult();
            
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Debug.Log($"[数据接收] 获取检测数据包，长度：{result.Count}字节");
                UpdateDetections(json);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                Debug.Log("[连接状态] 服务端主动断开连接");
                break;
            }
        }
    }

    /// <summary>
    /// 更新检测结果可视化
    /// </summary>
    void UpdateDetections(string json)
    {
        // 清空现有可视化元素
        foreach (Transform child in rawImage.transform)
        {
            Destroy(child.gameObject);
        }

        var detections = JsonUtility.FromJson<DetectionList>(json);
        if (detections == null || detections.detections == null)
        {
            Debug.LogError("[数据错误] 检测结果解析失败（数据结构异常）");
            return;
        }
        
        Debug.Log($"[数据分析] 有效检测目标数：{detections.detections.Length}");
        foreach (var detection in detections.detections)
        {
            DrawDetection(detection);
        }
    }

    /// <summary>
    /// 绘制单个检测结果
    /// 实现：坐标映射->UI元素实例化->样式配置
    /// </summary>
    void DrawDetection(Detection detection)
    {
        // 坐标系转换计算
        int videoWidth = videoPlayer.texture.width;
        int videoHeight = videoPlayer.texture.height;
        float canvasWidth = rawImage.rectTransform.rect.width;
        float canvasHeight = rawImage.rectTransform.rect.height;

        // 计算比例因子
        float scaleX = canvasWidth / videoWidth;
        float scaleY = canvasHeight / videoHeight;

        // 坐标映射计算
        float x = detection.box[0] * scaleX;
        float y = detection.box[1] * scaleY;
        float width = detection.box[2] * scaleX;
        float height = detection.box[3] * scaleY;

        // 实例化检测框预制体
        GameObject boxObj = Instantiate(Resources.Load<GameObject>("DetectionBox"));
        if (boxObj == null)
        {
            Debug.LogError("[资源错误] 检测框预制体加载失败");
            return;
        }

        // 配置UI元素
        boxObj.transform.SetParent(GameObject.Find("VideoDisplay").transform);
        RectTransform rt = boxObj.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(width, height);

        // 设置颜色样式
        Color boxColor, textColor;
        ColorUtility.TryParseHtmlString(detection.color, out boxColor);
        textColor = boxColor;
        boxColor.a = 0.6f; // 设置60%不透明度
        boxObj.GetComponent<Image>().color = boxColor;

        // 创建标签文本
        GameObject textObj = new GameObject("Label");
        textObj.transform.SetParent(GameObject.Find("VideoDisplay").transform);
        
        Text text = textObj.AddComponent<Text>();
        text.text = detection.color.ToUpper();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = textColor;
        text.fontSize = 24;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.LowerLeft;

        // 定位文本元素
        RectTransform textRt = textObj.GetComponent<RectTransform>();
        textRt.anchorMin = textRt.anchorMax = new Vector2(0, 0);
        textRt.anchoredPosition = new Vector2(70, 70);
        textObj.transform.SetAsLastSibling();
    }

    void OnDestroy()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
    }

    // 数据模型定义
    [System.Serializable]
    public class Detection
    {
        public int[] box;    // 检测框坐标[x,y,width,height]
        public string color; // 分类颜色编码
    }

    [System.Serializable]
    public class DetectionList
    {
        public Detection[] detections; // 检测结果集合
    }
}