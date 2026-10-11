# 工业视觉软件项目架构设计文档 (最终版 V3.0：流程图驱动与RPC核心)

## 1. 项目概述

本项目旨在设计并实现一款高性能、模块化、可扩展的工业视觉软件平台。为满足现代工业对灵活性和智能化的要求，本项目采用 **gRPC 远程过程调用 (RPC)** 机制，实现 C# 表现层与 C++ 核心服务的彻底解耦，并提供**本地/云端推理服务双模式支持**。

### 1.1 核心目标

1.  **通用兼容性：** 兼容市面上通用的工业相机（如支持 GenICam 标准的 GigE Vision、USB3 Vision 相机）。
2.  **模块化与服务化：** 将 C++ 核心封装为独立的服务进程 (**Vision Core Service**)，通过 gRPC 接口暴露所有功能。
3.  **流程图驱动：** C# UI 实现**拖拉拽流程图**功能，作为**业务逻辑控制中心**，负责统领和编排所有业务逻辑。
4.  **双模式推理：** 深度学习推理支持**本地高性能推理**和**云端推理服务**两种模式，可灵活切换。
5.  **非标检测能力：** 重点集成和封装深度学习模型，实现高效、准确的非标目标检测。

### 1.2 技术栈概览

| 模块 | 技术/语言 | 职责描述 |
| :--- | :--- | :--- |
| **用户界面 (UI)** | C# / WPF (推荐) | **流程图驱动的业务逻辑控制中心**，作为 gRPC Client 调用核心服务，负责流程编排、结果显示和用户交互。 |
| **流程图组件** | **NodeNetwork (推荐)** | 实现拖拉拽节点编辑功能，生成流程图数据结构。 |
| **核心服务 (Core Service)** | C++ / gRPC Server | 负责高性能图像处理、算法调度、相机数据流管理、外部通信，并通过 gRPC 提供服务。 |
| **通信协议 (IPC/RPC)** | **gRPC (Protocol Buffers)** | 实现 C# UI (Client) 与 C++ 核心服务 (Server) 之间的高效、跨语言、跨进程通信。 |
| **深度学习推理** | ONNX Runtime/TensorRT (本地) 或 gRPC Client (云端) | 专注于非标、复杂缺陷的检测模型训练与推理。 |

## 2. 整体架构设计：RPC 驱动的服务化架构

本项目采用服务化架构，将高性能的 C++ 核心逻辑独立为一个 **Vision Core Service** 进程，通过 gRPC 接口实现与 C# UI 的通信。

### 2.1 架构分层

1.  **表现层 (Presentation Layer):**
    *   **技术：** C# (WPF/WinForms)。
    *   **职责：** 用户界面、**流程图编辑**、参数配置、实时图像显示、结果可视化、日志记录。
    *   **通信：** 作为 **gRPC Client**，通过 Protobuf 序列化数据，调用核心服务。
2.  **应用层 (Application Layer):**
    *   **技术：** C++ / gRPC Server。
    *   **职责：** **gRPC 服务实现**。该层接收 C# UI 的请求，并将请求转发给对应的核心模块，负责数据格式转换和业务逻辑的协调。
3.  **核心服务层 (Core Service Layer):**
    *   **技术：** C++。
    *   **职责：** 相机驱动管理、图像采集、高性能图像处理 (OpenCV)、深度学习推理调度、外部设备通信。

### 2.2 核心模块划分

| 模块名称 | 核心技术 | 交互方式 | 主要功能 |
| :--- | :--- | :--- | :--- |
| **Vision Core Service** | C++ / gRPC Server | C# (gRPC Client) | 核心服务进程，包含所有高性能逻辑，对外提供统一的 gRPC API。 |
| **Camera Manager** | C++ / GenICam SDK | gRPC 接口 | 图像采集、参数设置、多相机同步，通过 gRPC Streaming 向 UI 推送实时图像。 |
| **Algorithm Core** | C++ / OpenCV | gRPC 接口 | 图像预处理、传统视觉算法执行、标定计算、结果处理。 |
| **Inference Dispatcher** | C++ / 策略模式 | gRPC 接口 | 根据配置，调度 **本地推理引擎** 或 **云端推理服务**。 |
| **Communication Manager** | C++ / 标准化 DLL | gRPC 接口 | 实现与 PLC/机器人的数据交互、信号触发和结果反馈。 |
| **Calibration Module** | C++ / OpenCV | gRPC 接口 | 负责相机内参、外参、手眼标定等，提供坐标转换服务。 |

## 3. C# UI 的流程图驱动与 gRPC 编排 (新增与深化)

C# UI 的核心职责是通过流程图实现**业务逻辑的动态配置和运行时编排**。

### 3.1 流程图组件选型与集成

*   **技术选型：** 推荐使用 **NodeNetwork** 或类似框架实现流程图的拖拉拽编辑功能。
*   **节点设计：** 流程图中的每个节点都应对应一个或一组 **Vision Core Service** 提供的 gRPC 原子功能。
    *   **输入节点：** 相机采集、文件读取、PLC 触发。
    *   **处理节点：** 图像预处理、边缘检测、AI 推理、坐标转换。
    *   **输出节点：** 结果显示、IO 输出、日志记录。

### 3.2 流程图的运行时编排机制

C# UI 内部需要新增一个 **Flow Executor** 模块，负责将流程图的静态配置转换为动态的 gRPC 调用序列。

| 模块 | 职责 |
| :--- | :--- |
| **流程图 UI (NodeNetwork)** | 负责生成和保存流程图的**数据结构**（如 JSON/XML 格式）。 |
| **Flow Executor (C#)** | **运行时核心：** 读取流程图数据结构，解析节点和连接关系，按顺序执行对应的 gRPC 调用。 |
| **gRPC Client (C#)** | 负责与 C++ Core Service 进行通信。 |

#### 3.2.1 流程图到 gRPC 调用的映射示例

流程图中的连接关系决定了数据流和控制流。Flow Executor 负责将数据从一个节点的 gRPC 响应，传递给下一个节点的 gRPC 请求。

| 流程图操作 | Flow Executor (C#) 动作 | gRPC 调用链 |
| :--- | :--- | :--- |
| **节点：相机采集** | 1. 调用 `CM_GrabSingleFrame()` | C++ 返回 `ImageFrame` |
| **连接：采集 -> 预处理** | 2. 将 `ImageFrame` 作为参数 | `client.Algo_PreProcess(ImageFrame)` |
| **连接：预处理 -> AI 推理** | 3. 将预处理结果作为参数 | `client.RunInspection(ProcessedImage)` |
| **连接：AI 推理 -> IO 输出** | 4. 将 `RunInspectionResponse` 转换为 `InspectionResult` | `client.IO_SendResult(InspectionResult)` |

#### 3.2.2 流程图执行器 (Flow Executor) 实现细节

Flow Executor 是 C# UI 侧的核心逻辑，它将静态的流程图配置转化为动态的 gRPC 调用序列。

**1. 核心设计模式：**

*   **解释器模式 (Interpreter Pattern):** Flow Executor 充当解释器，将流程图数据结构（如 JSON/XML）解析为一系列可执行的命令对象（如 `GrabFrameCommand`, `RunAlgoCommand`）。
*   **状态机 (State Machine):** 整个检测流程可以视为一个状态机，每个节点执行成功后，根据连接关系转移到下一个节点状态。

**2. 数据结构解析与执行：**

*   **流程图数据结构:** 包含 `Nodes` 列表和 `Connections` 列表。
    *   `Node` 属性：`ID`, `Type` (对应 gRPC 方法名), `Parameters` (gRPC 请求参数), `OutputDataKey`。
    *   `Connection` 属性：`SourceNodeID`, `SourceOutputPort`, `TargetNodeID`, `TargetInputPort`。
*   **执行上下文 (ExecutionContext):** 用于在节点之间传递数据。它是一个键值对存储 (`Dictionary<string, object>`)，用于存储上一个节点 gRPC 调用的响应数据，供下一个节点作为请求参数使用。

**3. 运行时执行逻辑：**

1.  **触发:** 接收到 PLC 触发信号 (`IO_WaitForTrigger` gRPC 响应) 或用户点击“运行”按钮。
2.  **拓扑排序:** 对流程图节点进行**拓扑排序**，确定无环图中的执行顺序。对于多分支并行路径，使用异步任务 (`Task.WhenAll`) 实现并行执行。
3.  **节点执行:** 遍历排序后的节点列表：
    *   **参数准备:** 根据节点的 `Parameters` 定义和 `ExecutionContext` 中的数据，组装 gRPC 请求消息。
    *   **gRPC 调用:** 调用对应的 gRPC Client 方法（例如 `client.RunInspection(...)`）。
    *   **结果存储:** 将 gRPC 响应数据解析后，存储到 `ExecutionContext` 中，使用 `OutputDataKey` 作为键。
4.  **流程结束:** 所有节点执行完毕，或遇到错误处理节点。

**4. 错误处理与日志：**

*   每个 gRPC 调用都应包含 `try-catch` 块，捕获 gRPC 状态码和异常。
*   错误信息应记录到 C# UI 的日志系统，并在流程图上以红色高亮显示出错的节点，实现**可视化调试**。

#### 3.2.3 示例流程图：非标缺陷检测与结果反馈

以下是一个典型的工业视觉检测流程，展示了流程图如何编排 gRPC 调用：

| 步骤 | 流程图节点 | 对应 gRPC 调用 | 数据流 (ExecutionContext) |
| :--- | :--- | :--- | :--- |
| **1** | **等待触发** (Trigger) | `IO_WaitForTrigger()` | 触发信号 |
| **2** | **图像采集** (Grab) | `CM_GrabSingleFrame()` | `ImageFrame` (图像数据) |
| **3** | **图像预处理** (PreProcess) | `Algo_PreProcess(ImageFrame)` | `ProcessedImage` (预处理后的图像) |
| **4** | **AI 推理** (Inference) | `RunInspection(ProcessedImage)` | `RunInspectionResponse` (检测结果列表, 状态) |
| **5** | **坐标转换** (Calibrate) | `CALIB_PixelToWorld(DetectionResult)` | `InspectionResult` (包含世界坐标 X, Y) |
| **6** | **结果反馈** (Feedback) | `IO_SendResult(InspectionResult)` | OK/NG 信号发送给 PLC |
| **7** | **结果显示** (Display) | (C# UI 内部逻辑) | 在 UI 上显示图像和检测框 |

**关键点：** 步骤 5 (坐标转换) 依赖于步骤 4 (AI 推理) 的结果，Flow Executor 确保了这种数据依赖和执行顺序。

流程图中的连接关系决定了数据流和控制流。Flow Executor 负责将数据从一个节点的 gRPC 响应，传递给下一个节点的 gRPC 请求。

| 流程图操作 | Flow Executor (C#) 动作 | gRPC 调用链 |
| :--- | :--- | :--- |
| **节点：相机采集** | 1. 调用 `CM_GrabSingleFrame()` | C++ 返回 `ImageFrame` |
| **连接：采集 -> 预处理** | 2. 将 `ImageFrame` 作为参数 | `client.Algo_PreProcess(ImageFrame)` |
| **连接：预处理 -> AI 推理** | 3. 将预处理结果作为参数 | `client.RunInspection(ProcessedImage)` |
| **连接：AI 推理 -> IO 输出** | 4. 将 `RunInspectionResponse` 转换为 `InspectionResult` | `client.IO_SendResult(InspectionResult)` |

### 3.3 C# UI 统领下的 PLC/机器人交互 (深化)

C# UI 负责所有 IO 交互的**时序控制**和**数据映射**。

#### 3.3.1 Protobuf 接口的全面化

为确保 C# UI 能完全控制 IO 交互，`VisionService` 必须暴露所有 IO 相关的原子操作：

```protobuf
// vision_service.proto (IO 交互部分补充)

service VisionService {
  // ... 其他方法 ...

  // 1. IO 交互：等待外部触发信号
  rpc IO_WaitForTrigger (IOTriggerRequest) returns (IOTriggerResponse);
  
  // 2. IO 交互：发送检测结果给外部设备
  rpc IO_SendResult (InspectionResult) returns (IOResponse);
  
  // 3. IO 交互：读取 PLC 请求的配方ID
  rpc IO_ReadRecipeID (IORequest) returns (IORecipeResponse);
  
  // 4. IO 交互：设置软件状态 (心跳/就绪)
  rpc IO_SetStatus (IOStatusRequest) returns (IOResponse);
}
```

#### 3.3.2 C++ Core Service 的 IO 兼容性设计

C++ Core Service 充当了 C# UI 和底层 `IO_Communication.dll` 之间的**适配器 (Adapter)**。

**设计目标：** 屏蔽底层协议细节，向上层 C# UI 提供统一的 gRPC 接口。

**C++ Core Service 内部实现伪代码：**

```cpp
// C++ (VisionServiceImpl.cpp) - IO_SendResult 实现

grpc::Status VisionServiceImpl::IO_SendResult(
    grpc::ServerContext* context, 
    const InspectionResult* request, 
    IOResponse* response)
{
    // 1. 将 Protobuf 结构体转换为 C++ 内部结构体
    InternalInspectionResult internalResult = ConvertFromProto(request);
    
    // 2. 调用底层 IO DLL 封装的函数
    // IO_Communication_DLL 是对 IO_Communication.dll 的 C++ 封装类
    int ret = IO_Communication_DLL::GetInstance().SendResult(
        internalResult.overall_status, 
        internalResult.target_x, 
        internalResult.target_y
    );
    
    // 3. 处理返回码并封装为 Protobuf 响应
    if (ret == 0) {
        response->set_success(true);
    } else {
        response->set_success(false);
        response->set_error_code(ret);
    }
    
    return grpc::Status::OK;
}
```

### 3.4 C# UI 统领下的标定流程

标定流程是高度交互性的，C# UI 负责引导用户完成每一步操作，并通过 gRPC 调用 C++ 核心的计算服务。

| 步骤 | C# UI (gRPC Client) 动作 | C++ Core Service (gRPC Server) 动作 | 职责分离说明 |
| :--- | :--- | :--- | :--- |
| **1. 启动标定** | 调用 `CALIB_StartIntrinsic()` | C++ 核心初始化标定状态和数据结构。 | C# 启动流程。 |
| **2. 采集图像** | 引导用户，调用 `CM_GrabSingleFrame()` | C++ 采集图像。 | C# 负责交互和时序。 |
| **3. 计算** | 调用 `CALIB_AddImageAndCompute(ImageFrame)` | C++ 核心内部调用 OpenCV `findChessboardCorners` 和 `calibrateCamera`。 | C++ 负责高性能计算。 |
| **4. 结果保存** | 调用 `CALIB_SaveParameters()` | C++ 核心将计算结果保存到配置文件。 | C# 触发保存。 |

## 4. 关键改进点：gRPC 架构与双模式推理

### 4.1 gRPC 协议引入与优势

gRPC 替代了传统的 P/Invoke 机制，实现了 C# UI 与 C++ 核心的彻底解耦。

*   **解耦与服务化：** C++ 核心可以独立部署，甚至部署在不同的机器或容器中，实现**分布式部署**。
*   **跨语言通信：** 基于 Protobuf，天然支持 C# 和 C++ 之间的高效、类型安全的数据交换。
*   **高性能数据流：** 利用 gRPC 的 **Server-side Streaming RPC**，C++ 核心可以高效、实时地将图像数据流推送到 C# UI 进行显示，解决了 P/Invoke 回调的复杂性和性能瓶颈。

#### 4.1.2 服务发现与负载均衡 (Service Discovery & Load Balancing)

为支持未来可能的高可用性 (HA) 或分布式部署需求，Vision Core Service 架构需内置服务发现和负载均衡机制。

**1. 服务发现 (Service Discovery):**

*   **单实例部署 (默认):** 采用**静态配置**。C# UI (Client) 通过读取本地配置文件（如 `appsettings.json` 或 `config.ini`）获取 Vision Core Service 的固定 IP 地址和端口。
*   **分布式部署 (可选):** 引入轻量级服务注册中心，如 **Consul** 或 **etcd**。
    *   **C++ Core Service:** 在启动时向注册中心注册自身的服务名称、IP 和端口，并定期发送心跳。
    *   **C# UI:** 作为服务消费者，通过注册中心查询可用的 Vision Core Service 实例列表。

**2. 负载均衡 (Load Balancing):**

gRPC 推荐使用**客户端负载均衡**。

*   **实现机制:** C# UI (Client) 在获取到多个 Vision Core Service 实例地址后，利用 gRPC 客户端库的内置功能或自定义逻辑进行请求分发。
*   **负载均衡策略:**
    *   **Round Robin (轮询):** 适用于所有 Core Service 实例性能均等的情况。
    *   **Least Connections (最少连接):** 适用于实例处理能力有差异或请求处理时间不一致的情况。
    *   **配置:** 客户端通过 gRPC 的 `NameResolver` 和 `LoadBalancer` 接口实现自定义策略。

### 4.2 深度学习推理调度器 (Inference Dispatcher)

Inference Dispatcher 模块位于 C++ 核心服务内部，负责实现本地/云端推理的双模式切换。

#### 4.2.1 架构设计：策略模式

Dispatcher 采用**策略模式**，定义统一的推理接口 `IInferenceEngine`，并实现两个具体策略：

| 策略实现 | 描述 | 通信方式 |
| :--- | :--- | :--- |
| `LocalInferenceEngine` | 调用本地高性能 DLL (`DL_Inference_Engine.dll`)，使用 ONNX Runtime/TensorRT。 | C++ 内部 DLL 调用 |
| `CloudInferenceEngine` | 作为 gRPC Client，调用外部的云端推理服务（例如部署在云服务器上的 AI 模型）。 | gRPC (Client) |

## 5. 核心模块详细设计 (保持不变)

### 5.1 Camera Manager (相机兼容层)

*   **方案：** 在 C++ 核心层采用 **GenICam** 标准作为底层通信协议，并使用 **Camera Abstraction Layer (CAL)** 封装不同厂商的 SDK。
*   **数据流：** 图像数据在 C++ 核心中统一为 `cv::Mat` 格式，并通过 gRPC Streaming 实时推送到 C# UI。

### 5.2 Algorithm Core (图像处理与算法层)

*   **方案：** 基于 **OpenCV** 库在 C++ 核心中实现所有算法。
*   **参数调整：** C# UI 通过 gRPC 调用 C++ 核心的参数设置接口。C++ 核心接收新参数后，立即重新处理当前图像，并将结果图像通过 gRPC Streaming 反馈给 C# UI 实时显示。

### 5.3 Deep Learning Engine (本地推理 DLL)

*   **模型格式：** 统一使用 **ONNX** 格式。
*   **推理后端：** 内部使用 **ONNX Runtime** (主推) 或 **TensorRT** (高性能扩展)。
*   **接口：** 保持 C 风格接口，供 C++ 核心内部调用，实现高性能、低延迟的本地推理。

## 5.4 数据管理与持久化 (Data Management and Persistence)

数据管理是工业视觉系统的核心，涉及高性能的实时数据流和可靠的持久化存储。

### 5.4.1 核心数据类型与流转

本项目的数据可以分为**实时数据**和**持久化数据**两大类。

| 数据类型 | 描述 | 存储位置 | 流转机制 |
| :--- | :--- | :--- | :--- |
| **图像数据** | 实时采集的图像帧，包含原始图像和处理后的图像。 | C++ Core Service 内存 (`cv::Mat`) | gRPC Server Streaming (C++ -> C# UI) |
| **检测结果** | 算法执行后的检测框、缺陷类型、世界坐标等。 | C++ Core Service 内存 | gRPC Unary/Streaming Response (C++ -> C# UI) |
| **流程图配置** | 流程图的节点、连接关系、节点参数。 | C# UI 内存 | JSON/XML 文件持久化 |
| **系统配置** | 相机参数、IO 配置、网络设置等。 | C++ Core Service 内存 | YAML/INI 文件持久化 |
| **标定参数** | 相机内参、外参、畸变系数、转换矩阵。 | C++ Core Service 内存 | 专有二进制或 YAML 文件持久化 |
| **深度学习模型** | ONNX 格式的模型文件。 | 文件系统 | C++ Core Service 启动时加载 |

### 5.4.2 数据持久化策略

所有需要跨会话保留的数据（配置、标定、模型）均采用文件系统持久化，并由 C++ Core Service 负责加载和保存，以确保数据的一致性和安全性。

**1. 配置与标定数据：**

*   **存储格式:** 推荐使用 **YAML** 或 **JSON** 格式存储配置数据，便于人类阅读和版本控制。标定参数可使用 **OpenCV 的 FileStorage** 存储为 XML/YAML 或专有二进制格式，以保证加载效率和精度。
*   **职责划分:** C# UI 负责配置文件的编辑和触发保存操作（通过 gRPC 调用 `SetCameraParam` 或 `CALIB_SaveParameters`）。C++ Core Service 负责将接收到的参数写入到本地文件系统。

**2. 模型文件：**

*   **存储位置:** 统一放置在 `04_Config/Models/` 目录下。
*   **加载机制:** `Deep Learning Engine` 在 C++ Core Service 启动时，根据当前配置的配方 ID，加载对应的 ONNX 模型文件到 GPU/CPU 内存中，以实现最快的推理速度。

### 5.4.3 架构图优化：数据流示意

为了更清晰地展示数据流和持久化机制，我们优化了整体架构图的描述，强调了数据在 C# UI、C++ Core Service 和文件系统之间的流转关系。

**（此处应插入一张数据流架构图，强调 gRPC 通信和文件持久化）**

*   **实时数据流 (虚线):** 图像数据通过 gRPC Streaming 从 C++ Core Service 流向 C# UI。检测结果通过 gRPC Unary Response 返回。
*   **控制流 (实线):** C# UI 通过 gRPC Unary Request 向 C++ Core Service 发送控制命令和配置参数。
*   **持久化流 (粗线):** C++ Core Service 负责从文件系统加载和保存配置、标定和模型数据。

## 6. 项目目录结构与代码组织

项目结构保持清晰的职责分离，以适应服务化架构。

```
VisionMaster_Clone/
├── 01_Presentation/          # C# UI 项目 (gRPC Client)
│   ├── VisionUI/             
│   │   ├── ViewModels/       # MVVM 模式
│   │   ├── FlowExecutor/     # 新增：流程图解析和 gRPC 编排逻辑
│   │   ├── FlowDesigner/     # 新增：流程图 UI 组件 (如 NodeNetwork)
│   │   └── GrpcClient/       # Protobuf 生成的 C# 客户端代码
├── 02_Core/                  # C++ 核心项目 (gRPC Server, 生成 VisionCoreService.exe)
│   ├── Source/
│   │   ├── GrpcService/      # gRPC Server 实现 (VisionServiceImpl.cpp)
│   │   ├── Inference/        # Inference Dispatcher 和策略实现
│   │   ├── CameraManager/    # 相机管理模块实现
│   │   ├── AlgorithmCore/    # OpenCV 算法和标定实现
│   │   └── Adapter/          # 封装底层 DLL 的 C++ 适配器类 (如 IO_Communication_DLL)
│   └── Proto/                # .proto 文件定义
├── 03_ExternalModules/       # 外部动态链接库项目
│   ├── DL_Inference_Engine/  # 本地深度学习推理 DLL
│   └── IO_Communication/     # PLC/机器人通信 DLL
└── 04_Config/                # 配置文件、标定文件、模型文件
```

## 7. 附录 A: 关键 gRPC 接口规范 

本附录列出核心服务对外暴露的 gRPC 接口，作为 C# UI 与 C++ 核心的契约。

### A.1 VisionService (Protobuf 定义)

| RPC 方法 | 类型 | 描述 |
| :--- | :--- | :--- |
| `StreamImage` | Server Streaming | C++ Server 实时推送图像数据流到 C# Client。 |
| `RunInspection` | Unary | C# Client 发送图像，C++ Server 执行完整检测流程并返回结果。 |
| `SetInferenceMode` | Unary | 切换推理模式（本地/云端）。 |
| `ConnectCamera` | Unary | 连接指定 ID 的相机。 |
| `SetCameraParam` | Unary | 设置相机参数（如曝光、增益）。 |
| **`CALIB_RunCalibration`** | Unary | **执行标定流程（内参/外参/手眼）。** |
| **`CALIB_PixelToWorld`** | Unary | **像素坐标到世界坐标的转换服务。** |
| **`IO_SendResult`** | Unary | **将检测结果发送给 PLC/机器人。** |
| **`IO_WaitForTrigger`** | Unary | **等待外部触发信号。** |
| **`IO_ReadRecipeID`** | Unary | **读取 PLC 请求的配方ID。** |

### A.2 数据结构 (Protobuf 示例)

```protobuf
// 图像数据结构体
message ImageFrame {
  int32 width = 1;
  int32 height = 2;
  bytes data = 3; 
  int32 format = 4; 
}

// 检测结果结构体
message DetectionResult {
  int32 object_id = 1;
  int32 class_id = 2;
  float confidence = 3;
  float x_norm = 4;
  float y_norm = 5;
  float w_norm = 6;
  float h_norm = 7;
}

// IO 交互结果结构体 (用于 IO_SendResult)
message InspectionResult {
    int32 sequence_id = 1;
    int32 overall_status = 2; // 0: OK, 1: NG
    int32 defect_count = 3;
    float target_x = 4;     // 目标世界坐标 X
    float target_y = 5;     // 目标世界坐标 Y
    float target_angle = 6; // 目标角度
}

// 算法执行响应
message RunInspectionResponse {
  repeated DetectionResult results = 1; // 检测到的所有目标
  int32 overall_status = 2; // 0: OK, 1: NG
  float inspection_time_ms = 3;
}
```

## 8. 总结

最终架构通过 **gRPC 服务化**的设计，实现了 C# UI 作为**流程图驱动的业务统领**的架构目标。C# UI 负责流程的配置、解析和运行时编排，通过 gRPC 调用 C++ Core Service 提供的所有原子功能，包括高性能的图像处理、推理，以及非高性能的 IO 交互和标定计算。C++ Core Service 则通过内部的**适配器模式**，封装了底层 DLL 的兼容性细节，确保了整个系统的高内聚、低耦合和高可维护性。
