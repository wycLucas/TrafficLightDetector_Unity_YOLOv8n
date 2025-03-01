"""
基于WebSocket的视频流目标检测服务器
使用YOLOv8模型进行实时目标检测，通过WebSocket协议与客户端通信
"""

import websockets
import json
import cv2
from ultralytics import YOLO
import numpy as np
import asyncio

# 初始化YOLOv8目标检测模型
model = YOLO('model.pt')


async def handle_connection(websocket):
    """处理客户端WebSocket连接"""
    print("[系统通知] 客户端连接已建立")
    try:
        async for message in websocket:
            # 接收并处理视频帧数据
            print(f"[数据接收] 接收视频帧数据，数据大小：{len(message)}字节")

            # 将字节数据转换为OpenCV图像格式
            frame_np = np.frombuffer(message, np.uint8)
            frame = cv2.imdecode(frame_np, cv2.IMREAD_COLOR)

            if frame is None:
                print("[错误警告] 视频帧解码失败，跳过本帧处理")
                continue

            # 使用YOLO模型进行目标检测
            results = model(frame)

            # 解析检测结果
            detections = []
            for r in results:
                boxes = r.boxes
                for box in boxes:
                    # 获取边界框坐标和类别信息
                    x1, y1, x2, y2 = map(int, box.xyxy[0])
                    w = x2 - x1
                    h = y2 - y1
                    cls = int(box.cls[0])
                    label = r.names[cls]

                    # 构建检测结果字典
                    detections.append({
                        "box": [x1, y1, w, h],
                        "color": label
                    })

            # 发送结构化检测结果
            print(f"[数据发送] 正在发送检测结果，包含{len(detections)}个目标")
            await websocket.send(json.dumps({"detections": detections}))

    except websockets.exceptions.ConnectionClosedError:
        print("[系统通知] 客户端连接异常断开")


async def main():
    """启动WebSocket服务器"""
    async with websockets.serve(
            handle_connection,
            host="localhost",
            port=8700
    ):
        print("[系统状态] WebSocket服务已启动")
        print("[网络配置] 监听地址：ws://localhost:8700")
        await asyncio.Future()  # 保持服务器持续运行


if __name__ == "__main__":
    asyncio.run(main())
