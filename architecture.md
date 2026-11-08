# 项目架构概览

## Flutter 集成更新（2026-09-06）

已新增 `flutter_app/` Android 宿主，以 Unity as a Library 复用现有数字人、EasyAR 和遮挡原型。Flutter 与全屏 Unity Activity 的原生桥接、离线人物预览、状态回传和暂停返回逻辑已实现；Flutter 测试和集成调试 APK 构建通过，Android 真机生命周期与公司现场 AR 仍待验收。

当前实现与运行步骤见 `DOC/flutter-unity-integration.md`、`flutter_app/README.md`。下方 2026-09-04 概览保留为历史基线，其中“Flutter 尚未接入”不再表示当前状态。

> Skill usage marker: used the project-teardown skill

## 当前全局概览（2026-09-04 校准）

### 一句话判断

项目已经从 EasyAR/URP 空壳推进为可构建、可在定位成功后生成 RobotExpressive 的 Unity Android AR 原型；当前处于“阶段 B 数字人视觉与交互骨架收口”，还不是包含 Flutter、真实 AI 语音和生产配置的完整 MVP。

### 项目本质

当前可运行产品仍是 Unity 独立 Android Development APK。Unity 负责 EasyAR Mega Block 定位、数字人空间放置、Animator、注视、调试交互和完整音频缓冲播放；Flutter 主应用、Unity as a Library 桥接和 AI 后端都尚未接入。

源码已经形成以下有效运行链路：

`Android 相机/IMU -> EasyAR Mega 云定位 -> MegaBlockController -> RobotExpressive -> 状态机/Animator/注视/音频播放`

项目目前最重要的业务问题，不再是“能否把模型放进 AR”，而是如何把这个技术原型收口成可信、可对话、可嵌入业务应用的数字人体验。

### 当前核心请求流

定位成功时，`EasyARCloudBlockBootstrap` 把 RobotExpressive 实例化为 Block 子对象并应用局部位置、旋转和缩放；定位丢失时隐藏，恢复后复用同一实例。数字人控制器随后初始化定位/会话状态、Animator、头部注视、AudioSource 和开发调试输入。

目前真正闭合的是“定位后显示并驱动数字人”的本地演示链路。语音输入、网络传输、ASR/LLM/RAG/TTS、口型/表情以及 Flutter 页面生命周期尚未进入闭环。

### 与原实施阶段对照

| 原规划阶段 | 当前状态 | 判断 |
| --- | --- | --- |
| 基础工程与 EasyAR Android | 已完成主要代码和 APK 构建 | 有 2026-09-04 成功构建产物；用户反馈已看到真机数字人，但现场定位、稳定性和性能数据尚未形成正式验收记录 |
| 阶段 A：Flutter / Unity as a Library | 未开始 | 没有 Flutter 工程、导出库、原生桥接或双向 JSON 生命周期实现 |
| 阶段 B：数字人替换绿色方块 | 部分完成，约 60%-70% | 模型、Block 锚定、基础状态机、Animator、手势、注视和音频播放骨架已完成；独立状态动画、口型、表情、性能验收和真实遮挡未完成 |
| 阶段 C：AI 语音对话 | 仅框架，约 15%-20% | 只有传输接口、会话状态和整段 PCM 播放；没有麦克风采集、WebSocket 实现、流式 TTS、重连/超时/打断协议或真实 AI 后端 |
| 阶段 D：多地点与生产化 | 基本未开始 | 仍是单 Block、调试 APK、测试包名和客户端长期凭据；没有配置后台、短期 Token、AAB/正式签名、隐私审计和设备兼容矩阵 |

按当前 Unity 独立原型计算，完成度约 65%；按原计划中的完整 Flutter + AR + AI MVP 计算，整体约 30%。百分比是基于交付面和验收闭环的工程估计，不代表工时线性消耗。

### 当前最核心的架构思想

现有实现刻意把定位状态与会话状态分开：EasyAR 负责“数字人在哪里”，会话层负责“数字人在做什么”。这种边界是正确的，因为短暂丢失定位时可以隐藏人物并暂停表现，而不必销毁数字人或清空未来的 AI 会话。

原计划按 A -> B -> C -> D 推进，实际改为先稳定 Unity/EasyAR 与数字人，再接 Flutter。这是有意识的风险收敛，但也意味着不能因为阶段 B 已经可见，就把阶段 A 视为完成。

### 最值得先理解的四个机制

1. **Mega Block 坐标闭环**：定位结果提供 Block 坐标系，数字人必须作为其子对象使用局部 Transform，才能固定在真实地点。
2. **定位与会话双状态机**：空间跟踪的 Found/Lost 与 Idle/Listening/Thinking/Speaking 不能混成一套状态。
3. **真实与虚拟画面合成**：当前只有摄像头 RGB 背景和虚拟物体深度，缺少真实手/人体遮挡，这是阶段 B 新暴露出的关键体验缺口。
4. **跨边界生命周期**：后续 Flutter、Unity、相机、麦克风和 WebSocket 必须共享明确的打开、暂停、恢复和释放协议，否则阶段 A 与 C 会互相制造故障。

### 当前大图总结

项目已经证明了 SDK 接入、Android 构建、Block 下动态放置数字人和基础角色控制的可行性。现在的形态是“Unity AR 数字人技术原型”，而不是可交付 MVP：视觉真实感还缺遮挡，角色表达还缺专用动画/口型/表情，对话只有接口，Flutter 和生产安全边界尚未落地。

当前记录还存在证据缺口：项目目录不是 Git 仓库，没有自动化测试；当前没有 Android 设备连接，无法从本机补取最近一次现场日志。用户已经反馈骨骼显示正常并观察到手部遮挡错误，这应记为真机进展，但仍需补充可复查的定位、丢失恢复、帧率和温度记录。

### 下一步最值得优先深挖的方向

优先收口阶段 B 的 AR 视觉验收：补上真实手/人体遮挡，并用同一台目标 Android 设备重新验证定位稳定、人物尺寸/朝向、Found/Lost 恢复、动画与性能。该闭环稳定后，应回到原阶段 A，先验证最小 Flutter as a Library 集成和相机生命周期；随后再实现阶段 C 的麦克风 + WebSocket + 流式 TTS + 口型，避免把跨引擎生命周期风险拖到最后。

## 历史全局概览（初始扫描，已过期，仅供追溯）

### 一句话判断

这是一个基于 Unity 6.0.3、URP 的空白 3D 模板工程，已经准备了 EasyAR Sense/Mega 的本地安装包，但当前场景和代码尚未形成可运行的 AR 业务功能。

### 项目本质

项目的实际运行单元是 `Assets/Scenes/SampleScene.unity`。场景目前只有 Unity 默认的主相机、方向光和全局 Volume；渲染由 URP 资产提供，脚本资产主要是 Unity 模板自带的 Readme 编辑器，不包含业务运行时代码。

`Packages/manifest.json` 启用了 URP、Input System、AI Navigation、Timeline、UGUI、Visual Scripting 等 Unity 包。EasyAR 的三个 `.tgz` 包位于 `Packages/EasyARSenseUnityPluginForMega_4003.1.0+5282.db47c2304`，但没有被 manifest 引用，也没有在 PackageCache 中发现已安装的 EasyAR 包。

### 它当前解决的业务痛点

当前版本还没有实现具体业务痛点。它解决的是“建立一个可编译、可编辑的 Unity URP 起点”，并把未来可能使用的 EasyAR Mega 空间/云端 AR 能力放在项目旁边，等待通过 UPM 导入。

因此，不能把 EasyAR 压缩包的能力描述成项目已经具备的能力；目前尚未看到相机取流、平面/图像识别、空间定位、地图加载、锚点同步或交互逻辑。

### 一个核心请求/任务流

当前唯一可确认的运行流是：

`Unity 启动 -> 加载 SampleScene -> Main Camera/Directional Light/Global Volume 初始化 -> URP 渲染默认空场景`

Input System 资产包含 Unity 通用的 Navigate、Submit、Cancel、Point、Click 等动作及键鼠、手柄、触摸、摇杆、XR 控制器绑定，但场景中没有发现消费这些动作的业务脚本或 UI。

### 最核心的架构思想

现阶段的核心思想不是复杂的运行时架构，而是“Unity 编辑器配置与场景资产驱动”：项目行为主要由场景序列化数据、URP Render Pipeline 资产和 Package Manager 依赖决定，运行时代码几乎为空。

EasyAR 部分目前处于“外部能力候选项”阶段。若后续导入，它更可能成为相机/空间感知层，再由项目自己的场景对象和交互脚本承接 AR 内容；这条边界尚未在当前代码中实现。

### 最值得先理解的 2-4 个机制

1. **Package 安装边界**：本地 `.tgz` 文件、`manifest.json` 和 `Library/PackageCache` 分别代表“可供安装”“声明依赖”“已解析可用”，三者当前并未闭合。
2. **场景作为运行时入口**：Build Settings 只启用 `SampleScene`，所以任何未来 AR 初始化都必须从该场景或其引用的 Prefab/组件进入。
3. **URP 配置链**：PC/Mobile Render Pipeline Asset、Renderer 和 Volume Profile 决定默认渲染行为，是以后叠加摄像头背景、透明合成和移动端性能约束的基础。
4. **输入抽象层**：Input System 已定义跨设备动作，但尚无动作消费者；后续 AR 交互需要明确触摸/点击与 XR 输入如何映射到场景操作。

### 当前大图总结

项目现在更接近“URP 空模板 + 待安装 EasyAR Mega SDK”，而不是“EasyAR AR 应用”。可确认的实现集中在 Unity 工程骨架：版本、包依赖、构建场景、渲染资产和模板 Readme。真正的 AR 业务边界、数据流和用户任务流尚未出现。

### 下一步最值得优先深挖的方向

优先拆解 **EasyAR 包的导入与 AR 场景初始化边界**：先确认三个包的依赖关系、导入后会提供哪些运行时组件，再决定相机、空间地图/定位和内容锚定应放在哪一层。只有这条链路成立后，才有意义继续分析追踪稳定性、交互输入或移动端性能。

## 后续主题拆解（预留）

### 包管理与 EasyAR 导入边界

#### 本轮唯一问题：地图如何变成数字人的稳定坐标系

这里最容易产生一个误解：地图文件本身不会“包含并导入数字人”。地图负责回答“设备当前在已建空间的哪个位置”，数字人是普通 Unity Prefab，必须挂到 EasyAR 输出的 Block/Map Transform 下，才能随着定位结果稳定显示。

先按你手里的建图产物分流：

- **有 Mega Studio 导出的 `.ema` 文件**：优先参考包内 `MegaBlock_Ema` 示例。`.ema` 描述一个或多个 Block 及其注释节点，运行时从 `StreamingAssets` 读取、用 `EmaDecoder.Decode` 解析；每个 Block 建立一个 `MegaBlockController`，节点的位姿通过 `node.transform.ToUnity()` 转成 Unity 局部坐标。数字人应作为节点的替代物实例化并设为该节点的子对象。
- **只有云端地图/Block ID**：使用 `MegaTrackerFrameFilter`（云定位）和 `MegaBlockController`。在 Controller 的 `Source` 中配置 `BlockInfoSourceData(ID, Name)`，再把 `Tracker` 指向场景里的 `MegaTrackerFrameFilter`。插件会下载/加载 Block，成功后触发 `TargetLoad`，定位到后触发 `TargetFound`；数字人应在 `TargetFound` 后显示，并挂在这个 Controller 下。
- **只有 Sparse Spatial Map 的 Map ID**：这不是 Mega Block 链路，应使用 `SparseSpatialMapTrackerFrameFilter` + `SparseSpatialMapController.MapManagerSourceData(ID, Name)`。数字人同样挂在 `SparseSpatialMapController.transform` 下，但它得到的是稀疏空间地图坐标，不能拿 Mega Block 的 Controller 混用。

#### 推荐的运行时链路

`ARSession + 相机帧源 -> MegaTrackerFrameFilter -> 云端定位 -> MegaBlockController 加载/跟踪 -> 数字人 Prefab 挂到 Controller -> TargetLost 时隐藏或暂停`

场景至少需要从包示例复制并检查这些角色：`ARSession`、可用的 Camera/FrameSource、`MegaTrackerFrameFilter`、一个或多个 `MegaBlockController`，以及用于显示摄像头画面的渲染组件。当前项目的 `SampleScene` 只有默认相机和灯光，不能直接承载这条链路。

#### `.ema` 场景中添加数字人的实质

`MegaBlock_Ema` 示例已经给出了正确的坐标关系：先为每个 `ema.blocks` 创建 Controller，再遍历 `ema.annotations`；对有 `BlockParent` 的节点，把占位物设为 Block 的子对象，并写入节点的局部位置、旋转、缩放。把示例里的 Cube/Panda 占位物替换成你的数字人 Prefab 即可。数字人根节点的原点、朝向和单位必须先在 DCC/Unity 中校准，否则会出现脚陷地、面向反向或尺寸异常。

该示例有明确边界：只演示 `cube`/`point` 几何，忽略 `WorldParent`、`keepTransform` 和节点 `location`。如果你的 EMA 标注使用了这些能力，不能照抄示例，需要先定义世界坐标节点如何与 Block 坐标合并。

#### 配置与故障判断

`MegaTrackerFrameFilter` 要在 Session 启动前配置服务来源（全局配置、API Key 或 Token）及正确的 Mega 服务类型。非试用 Block 还需要含 Mega 授权的 EasyAR License；服务鉴权失败、Block ID 不存在、网络下载失败和设备不支持，会分别通过 Session/TargetLoad/Localization 状态暴露，不能只靠“数字人没有出现”来排查。

最小验证顺序应是：先确认 EasyAR 包已通过 UPM 安装并能运行官方 Mega 示例；再确认日志出现 Block 加载成功和定位 `Found`；最后才把数字人挂入坐标层。否则把模型、动画或渲染问题与地图定位问题混在一起，会很难定位。

#### 云端 Block ID 的具体落地方案

你当前属于第二种分支：云端地图/Block ID。推荐以 `MegaBlock_Basic` 为基线，不使用 `MegaGo`（后者面向本地 target 文件）。

运行时关系应保持为：

`ARSession -> CameraDeviceFrameSource/平台 FrameSource -> MegaTrackerFrameFilter -> MegaBlockController(ID) -> 数字人节点`

其中：

- `MegaTrackerFrameFilter` 负责把相机帧送到 Mega 云定位服务，并根据 `ServiceAccessSource` 读取全局配置、API Key 或 Token。
- `MegaBlockController.Source` 设置为 `BlockInfoSourceData(BlockId, BlockName)`；Source 必须在该组件 `Start` 前设置。
- `MegaBlockController.Tracker` 指向同一个 `MegaTrackerFrameFilter`。运行时 Controller 会向 Tracker 请求加载 Block，加载结果通过 `TargetLoad` 返回。
- Block 被服务定位后，Controller 的 `TargetFound` 事件才表示其 Transform 已成为当前地图坐标系；此时再显示数字人。

数字人不要在 `Awake` 中直接放到世界坐标。可以预先把数字人作为 Block Controller 的子对象，或在 `TargetFound` 时实例化并设置为子对象；使用 `SetParent(block.transform, false)` 保留局部坐标。`TargetLost` 时建议禁用渲染/动画，重新 `TargetFound` 时恢复，而不是销毁后反复下载模型。

最小控制逻辑可以抽象成：

```csharp
block.TargetFound += () => digitalHumanRoot.SetActive(true);
block.TargetLost += () => digitalHumanRoot.SetActive(false);
digitalHumanRoot.transform.SetParent(block.transform, false);
digitalHumanRoot.transform.localPosition = anchorOffset;
digitalHumanRoot.transform.localRotation = anchorRotation;
```

这段逻辑的前提是 Block 已成功加载；若 `TargetLoad` 返回 `Unauthorized`、`InvalidTarget` 或网络错误，`TargetFound` 不会发生。调试时应按顺序看：`ARSession.State` 是否到 `Ready`、`TargetLoad` 是否成功、`LocalizationRespond` 是否为 `Found`、最后才看数字人模型和动画。

当前工程还缺少上述全部 EasyAR 运行时对象，因此下一步不是先写数字人脚本，而是导入官方 `MegaBlock_Basic` Sample，验证同一个 Block ID 能在真机上定位，再将其 Session/FrameSource/Tracker/Controller 结构迁回 `SampleScene`。

#### 云端地图信息如何获取并加入 Unity

云端 Block 不需要、也不能按普通 FBX/Prefab 的方式“下载地图模型”到 Unity。Unity 需要保存的是三类信息：

1. **Block ID**：云端地图 Block 的唯一标识；Block Name 只是可选显示名称。
2. **服务鉴权**：App ID、API Key、API Secret，或由自有后端签发的 Token。
3. **服务地址**：通常使用默认地址；只有自建/切换服务端点时才需要填写自定义 URL。

获取方式有两种：

- **开发中心手动方式**：登录 EasyAR Develop Center，进入你创建地图/Block 的 Mega 管理页面，在 Block/地图详情中复制 Block ID；同时从应用的服务凭据页面取得 App ID、API Key、API Secret。不要把 API Secret 写进公开仓库或客户端日志。
- **Unity 编辑器查询方式**：安装 `com.easyar.sense` 与 `com.easyar.mega` 后，使用菜单 `EasyAR > Mega > AR Session (Mega Block Default Preset)` 创建基础会话；再用 `EasyAR > Mega > Target : Mega Block` 创建 Controller。在 `MegaBlockController` Inspector 的 Editor Tool 中选择服务、填入 API Key 认证信息，工具会从云端查询 Block 并把选中的 ID/名称写入 `Source`。该编辑器工具明确要求 API Key/API Secret，Token 不能用于编辑器查询；使用 Token 时应手动填写 Block ID，并在运行时配置 Token。

写入 Unity 后的最小检查项：

- `MegaTrackerFrameFilter.ServiceType` 为 `Block`。
- `ServiceAccessSource` 与实际凭据类型一致；若选 `GlobalConfig`，在 `EasyAR > Sense > Configuration` 的 `Global Mega Block Localization Service Config` 填写。
- `MegaBlockController.Source.ID` 是正确的 Block ID，`Tracker` 指向场景内的 `MegaTrackerFrameFilter`。
- `ARSession` 的 Assemble Options 包含对应 FrameSource 和 FrameFilter，且 Session 能进入 `Ready`。

运行时真正发生的是：Controller 根据 Block ID 向云端请求 Block 数据，Tracker 发送相机帧做定位，定位成功后更新 Controller 的 Transform。首次使用需要网络并可能产生服务请求，后续是否命中本地缓存由插件和服务策略决定；项目不应把“云端地图下载完成”当作本地静态资产处理。

#### 本次工程配置结果

已将本地 `com.easyar.sense` 与 `com.easyar.mega` `.tgz` 写入 `Packages/manifest.json`，并在 `SampleScene` 添加 `EasyARCloudBlockBootstrap`。该脚本使用 Mega Block 的 `MotionTracking_Inertial` 预设，在 Session 启动前配置 API Key 鉴权和指定 Block ID，监听定位状态，并在 `TargetFound` 时将 Inspector 指定的数字人 Prefab 挂到 Block Transform。

当前仍需在 Unity 包解析完成后进行真机验证，并在 Inspector 的 `Digital Human Prefab` 槽位指定实际模型。API Secret 已写入本地场景/脚本配置；项目提交或分享前应移除、改用后端 Token，并对已暴露的凭据进行轮换。

### AR 场景初始化与请求流

待后续补充：相机权限、相机帧、跟踪会话、空间地图/锚点和场景内容之间的生命周期关系。

### 输入与交互

待后续补充：Input System 通用动作如何转换为触摸、点击、XR 控制器及 AR 内容操作。

### 渲染与移动端部署

待后续补充：URP 的 PC/Mobile 配置、摄像头背景合成、设备能力差异、Android/iOS 构建设置和性能瓶颈。
