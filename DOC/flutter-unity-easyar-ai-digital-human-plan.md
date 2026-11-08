# Flutter + Unity + EasyAR AI 数字人技术方案

## 1. 文档信息

- 文档日期：2026-09-02
- 当前结论：方案可行，建议 Android 优先验证
- 推荐路线：Flutter 主应用 + Unity AR 子模块 + EasyAR Mega 定位 + AI 对话后端
- 当前阶段：技术方案设计，尚未开始 Flutter 集成

## 2. 项目目标

在已经完成建图的指定地点，通过 EasyAR Mega 对手机进行空间定位；定位成功后，在预设空间坐标显示一个风格化、拟人化的 3D 数字人。用户可以与数字人进行语音对话，数字人能够播放回答语音，并同步嘴型、表情和动作。

目标体验如下：

```text
用户进入 Flutter 应用
  -> 选择地点或业务场景
  -> 打开 Unity AR 页面
  -> EasyAR 在真实地点完成云定位
  -> Unity 在指定锚点显示数字人
  -> 用户说话
  -> AI 理解并生成回答
  -> 数字人播放语音、嘴型和动作
```

## 3. 可行性结论

整体可行性为中高。项目中风险最高的 EasyAR Android 真机定位链路已经跑通，后续主要风险集中在 Flutter 与 Unity 的集成、应用生命周期、相机与麦克风管理、AI 对话延迟以及正式环境的密钥安全。

需要明确：EasyAR 不直接提供 Flutter SDK。Flutter 可以作为应用主体，但 EasyAR Mega 和数字人的 3D 渲染仍然运行在 Unity 中。

### 3.1 官方能力依据

[EasyAR 官方下载页](https://www.easyar.cn/view/download.html)当前提供：

- EasyAR Sense Unity Plugin (for Mega) 4003.1.0
- EasyAR Sense Unity Plugin 4003.1.0
- EasyAR 原生 SDK 4.10.0
- Mega Studio、Mega Toolbox 等配套工具
- Unity、原生 Android/iOS、小程序和 XR 相关能力

官方页面没有 Flutter SDK 或 Flutter 插件分类。页面对 Mega Unity 插件的定位是开发超大空间 AR 体验，并提供 Mega Block 模型查看、内容辅助摆放和远程验证能力。

原生 EasyAR Sense SDK 可用于自研 3D 引擎或少量 3D 渲染，但如果采用该路线，需要重写现有 Unity 相机渲染、空间坐标、模型、动画和交互逻辑，不适合本项目当前阶段。

### 3.2 当前工程基础

当前工程已经具备以下基础：

- Unity 6000.3.23f1 工程
- EasyAR Sense Unity Plugin 4003.1.0
- EasyAR Mega Support 2.14.0
- EasyAR Mega Block 云定位链路
- Android ARM64 APK 构建链路
- 定位成功后生成绿色测试方块的验证逻辑
- 定位丢失后隐藏内容、重新定位后恢复内容的基础逻辑

相关文件：

- `Assets/Scripts/EasyARCloudBlockBootstrap.cs`
- `Assets/Editor/AndroidApkBuilder.cs`
- `Packages/manifest.json`
- `Builds/android-build-summary.txt`

## 4. 系统架构

```text
┌─────────────────────────────────────────────┐
│ Flutter 主应用                              │
│ 登录、用户、地点列表、业务页面、会话入口    │
│ 获取 PlaceConfig、业务 Token、EasyAR Token  │
└──────────────────┬──────────────────────────┘
                   │ 低频 JSON 消息
┌──────────────────▼──────────────────────────┐
│ Unity AR 子模块                             │
│ EasyAR 相机、Mega Block 定位、空间锚点       │
│ 数字人渲染、动画、表情、嘴型、AR 页面交互    │
└──────────────────┬──────────────────────────┘
                   │ HTTPS / WebSocket
┌──────────────────▼──────────────────────────┐
│ AI 与业务后端                               │
│ ASR、LLM、地点知识库、TTS、内容安全          │
│ 会话管理、配置管理、临时令牌签发、日志       │
└─────────────────────────────────────────────┘
```

### 4.1 模块职责

| 模块 | 主要职责 | 不承担的职责 |
| --- | --- | --- |
| Flutter | 应用入口、登录、地点选择、业务页面、用户会话 | 不负责 AR 相机和 3D 数字人渲染 |
| Unity | AR 页面、EasyAR、数字人、动画、嘴型和空间交互 | 不保存长期业务密钥，不承担业务后台管理 |
| EasyAR | 相机输入、Mega Block 云定位、空间坐标输出 | 不生成 AI 回答，不创建数字人 |
| AI 后端 | ASR、LLM、RAG、TTS、令牌和会话 | 不负责终端 3D 渲染 |

## 5. 推荐集成方式

### 5.1 首版选择

使用 Unity as a Library，将 Unity Android 导出结果接入 Flutter 的 Android 工程。Flutter 负责打开一个全屏 Unity AR Activity，AR 页面内部的相机、数字人和交互 UI 由 Unity 管理。

首版不建议把 Unity 做成 Flutter 页面中的小尺寸 PlatformView，原因如下：

- 全屏 Activity 的相机生命周期更清晰
- Flutter 与 Unity 的渲染层冲突更少
- 返回、前后台切换和资源释放更容易验证
- 连续音频不需要经过高频跨引擎消息桥
- 当前 AR 体验本身就是一个完整页面

第三方 Flutter-Unity 插件可以用于技术验证，但生产方案不应默认依赖其对 Unity 6 的兼容性。生产实现应保留自有 Android 原生桥接层，以便控制 UnityPlayer 生命周期、构建版本和错误处理。

### 5.2 平台顺序

第一阶段只支持 Android。当前工程的 Android 真机定位与构建已经验证，先完成 Flutter Android 集成可以缩小风险范围。

iOS 需要单独完成 UnityFramework、Xcode、签名、权限和 EasyAR License 配置，不能因为 Flutter 支持 iOS 就认为 Unity/EasyAR 集成会自动支持 iOS。

## 6. 运行时流程

1. 用户在 Flutter 中登录并选择地点。
2. Flutter 请求后端，获取地点配置、业务会话 Token 和 EasyAR 临时 Token。
3. Flutter 打开 Unity AR 页面，并传入地点配置。
4. Unity 创建 EasyAR Mega Block Session。
5. EasyAR 打开相机，并对现场画面进行云定位。
6. 定位成功后，Mega Block Transform 成为当前地点的空间坐标系。
7. Unity 根据锚点位置创建数字人 Prefab。
8. 数字人进入 Idle 状态，等待用户发起对话。
9. Unity 采集用户语音，通过 WebSocket 或流式 HTTP 发送到 AI 后端。
10. 后端完成 ASR、LLM/RAG 和 TTS。
11. Unity 接收回答音频、文本、嘴型时间轴和动作指令。
12. 数字人播放语音，并同步嘴型、表情和动作。
13. EasyAR 定位丢失时暂停交互或隐藏人物；重新定位后恢复。
14. 用户退出 AR 页面时，Unity停止相机、麦克风和网络会话并返回 Flutter。

## 7. 指定地点与锚点方案

### 7.1 地点配置

每个地点建议保存独立配置：

```json
{
  "schemaVersion": 1,
  "placeId": "museum_hall_a",
  "blockId": "easyar-block-id",
  "anchorId": "guide_position_01",
  "anchorPose": {
    "position": [0.0, 0.0, 0.0],
    "rotationEuler": [0.0, 180.0, 0.0],
    "scale": [1.0, 1.0, 1.0]
  },
  "avatarId": "guide_01",
  "dialogueProfileId": "museum_guide",
  "assetVersion": "1.0.0"
}
```

配置中只保存 Block ID，不保存 EasyAR API Secret。

### 7.2 锚点制作

MVP 阶段可以直接记录数字人相对 Block 的位置和旋转。正式阶段建议使用 Mega Studio 或 EMA 标注管理锚点，利用官方提供的内容辅助摆放和远程验证能力，避免坐标调整依赖重新修改代码和发版。

如果一个 Block 内有多个数字人位置，应使用多个 `anchorId`，而不是为每个位置重复创建相同地图。

## 8. 数字人方案

### 8.1 美术路线

推荐使用风格化 3D 数字人，而不是写实人物：

- 使用 VRoid Studio、Blender 或其他角色工具制作
- 使用 VRM 或 FBX 导入 Unity
- 在 Unity 中转换为 Prefab
- 使用 Humanoid 骨骼复用现成动画
- 保留基础面部 BlendShape，用于嘴型和表情

### 8.2 运行组件

```text
AIDigitalHuman
├─ AvatarController
├─ Animator
├─ AudioSource
├─ LipSyncController
├─ ExpressionController
├─ LookAtController
└─ ConversationController
```

### 8.3 最小状态机

| 状态 | 表现 |
| --- | --- |
| Idle | 待机、呼吸、轻微视线变化 |
| Listening | 面向用户、显示倾听动作 |
| Thinking | 思考动作，避免角色静止无反馈 |
| Speaking | 播放 TTS、嘴型、表情和手势 |
| Lost | 定位丢失后暂停或隐藏 |
| Error | 网络、权限或服务异常提示 |

### 8.4 移动端约束

- 单人物建议约 3 万至 8 万三角面
- 贴图以 1K/2K 为主
- 减少透明头发层和高开销皮肤材质
- 控制 SkinnedMeshRenderer、骨骼和材质数量
- 使用 URP 移动端兼容材质
- 模型原点位于脚底，单位按米处理，正面朝向统一

## 9. AI 对话方案

### 9.1 服务链路

```text
麦克风音频
  -> 流式 ASR
  -> 地点知识库检索
  -> LLM 生成回答
  -> 内容安全处理
  -> 流式 TTS
  -> 音频 + viseme/嘴型时间轴 + 动作标签
  -> Unity 数字人播放
```

AI 服务应放在后端，不在 Flutter 或 Unity 客户端保存服务商长期密钥。后端可以按 `dialogueProfileId` 为不同地点配置人物身份、知识库、回答风格和安全规则。

### 9.2 首版返回协议

```json
{
  "type": "assistant_response",
  "requestId": "request-id",
  "text": "欢迎来到展厅。",
  "audioUrl": "signed-audio-url",
  "emotion": "happy",
  "gesture": "welcome",
  "visemes": []
}
```

MVP 可以先使用音量驱动的简单嘴型；正式版本再使用音素或 viseme 时间轴获得更稳定的口型同步。

## 10. Flutter 与 Unity 通信协议

消息使用带版本号的 JSON，避免直接传递高频音频或视频数据。

### 10.1 Flutter 发送给 Unity

- `Initialize`
- `OpenPlace`
- `UpdateToken`
- `PauseAR`
- `ResumeAR`
- `CloseAR`

示例：

```json
{
  "version": 1,
  "type": "OpenPlace",
  "requestId": "request-id",
  "payload": {
    "placeConfig": {},
    "easyarToken": "short-lived-token",
    "businessToken": "short-lived-token"
  }
}
```

### 10.2 Unity 发送给 Flutter

- `UnityReady`
- `ARSessionReady`
- `LocalizationFound`
- `LocalizationLost`
- `AvatarReady`
- `ConversationStateChanged`
- `RecoverableError`
- `FatalError`
- `ARClosed`

所有错误应带错误码、可展示信息和可恢复标志，不能只依赖 Unity 日志。

## 11. 实施阶段

### 阶段 A：Flutter/Unity 集成验证

预计 3 至 5 个工作日。

工作范围：

- 创建最小 Flutter Android 应用
- 将 Unity 导出为 Library
- Flutter 打开和关闭全屏 Unity AR 页面
- 传递 Block 配置
- 保留当前绿色方块定位验证
- 验证前后台切换、锁屏、权限和异常返回

验收标准：

- Flutter 可以稳定进入和退出 AR 页面
- 连续进入和退出 10 次，无黑屏、崩溃和相机占用残留
- AR 定位效果与当前独立 Unity APK 基本一致
- 定位成功、丢失和错误状态可以回传 Flutter
- 返回 Flutter 后相机和麦克风被释放

### 阶段 B：数字人替换绿色方块

预计 3 至 5 个工作日，不包含定制美术制作时间。

工作范围：

- 导入一个风格化 3D 人物
- 创建数字人 Prefab
- 配置 Idle、Listening、Thinking、Speaking 动画
- 添加 AudioSource 和基础嘴型
- 配置 Block 锚点和人物朝向

验收标准：

- 定位成功后数字人在指定锚点出现
- 人物尺寸、脚底位置和朝向正确
- 定位丢失和恢复时没有重复实例或错位
- 目标 Android 设备上帧率和温度可接受

### 阶段 C：AI 语音对话

预计 1 至 2 周。

工作范围：

- 接入流式 ASR、LLM/RAG 和 TTS
- 实现语音会话状态机
- 实现语音播放、嘴型和基础动作
- 增加超时、取消、重试和弱网处理
- 增加地点专属知识库和人物设定

验收标准：

- 用户可以完成连续多轮语音对话
- 数字人状态与实际会话状态一致
- 用户可以中止当前回答
- 网络失败不会导致 Unity 卡死或会话无法退出
- 日志能够关联一次对话的 requestId

### 阶段 D：多地点与生产化

预计 1 至 2 周起，取决于地点数量和发布要求。

工作范围：

- 地点和锚点配置后台
- Mega Studio/EMA 内容摆放流程
- 数字人资源版本管理
- 设备兼容性测试和性能优化
- 权限、隐私、内容安全和审计
- Android AAB、签名和发布流程

## 12. 关键风险与处理策略

| 风险 | 影响 | 处理策略 |
| --- | --- | --- |
| EasyAR 没有 Flutter 官方插件 | 集成责任由项目承担 | 使用 Unity as a Library，先做 Android 技术验证 |
| Unity 6 与 Flutter Android 构建版本冲突 | 无法构建或运行 | 固定 Flutter、AGP、Gradle、JDK、NDK 和 Unity 导出版本 |
| 相机生命周期异常 | 黑屏、相机被占用 | 使用全屏 Unity Activity，建立明确的进入/退出状态机 |
| 客户端暴露 API Secret | 服务被盗用 | 后端签发短期 Token，并轮换已经暴露的凭据 |
| 包名与 EasyAR License 不匹配 | EasyAR 初始化失败 | Flutter applicationId 与许可证统一，发布前自动校验 |
| 云定位不稳定 | 数字人漂移或无法出现 | 规范建图、现场光照、定位引导和 Lost 恢复策略 |
| AI 回答延迟 | 对话体验差 | 使用流式 ASR/LLM/TTS，并在 Thinking 状态提供反馈 |
| 弱网或断网 | EasyAR 与 AI 都不可用 | 超时、重试、明确提示和有限降级内容 |
| 数字人模型过重 | 掉帧、发热和闪退 | 移动端模型预算、LOD、材质和纹理优化 |
| 构建警告数量较多 | 隐藏兼容问题 | 正式集成前分类审查现有构建警告 |
| 隐私与内容合规 | 上架或运营风险 | 相机/麦克风授权、隐私政策、数据保留和内容安全机制 |

## 13. 安全要求

- EasyAR API Secret 不得写入 Unity 场景、C# 脚本或 Flutter 资源
- AI 服务商密钥不得放入客户端
- EasyAR 使用 SDK 已支持的 `TokenAccessData` 接入临时 Token
- 所有 Token 应有有效期、使用范围和撤销机制
- 日志不得打印 Token、语音原文敏感字段或用户隐私信息
- 当前已经进入客户端的长期凭据应在正式开发前轮换

## 14. MVP 范围

MVP 包含：

- Android Flutter 主应用
- 一个 EasyAR Mega Block 地点
- 一个固定锚点
- 一个风格化 3D 数字人
- 待机、倾听、思考和说话动画
- 多轮语音对话
- 一个地点专属知识库
- 定位、网络和权限错误提示

MVP 暂不包含：

- iOS
- 多数字人同场
- 实时全身动作捕捉
- 复杂路径行走与避障
- 动态布料和高成本毛发渲染
- Flutter UI 覆盖在 Unity 画面之上
- 离线大模型和离线云定位

## 15. 工作量预估

在现有 EasyAR 真机流程保持可用、AI 服务可以直接调用、角色资产已经准备好的前提下，小团队完成可演示 MVP 预计约 3 至 5 周。

该时间不包含：

- 高质量定制角色的美术制作
- 大量地点的采集和建图
- AI 服务采购和商务流程
- iOS 适配
- 应用商店审核时间

## 16. 最终建议

采用以下路线：

```text
Flutter 负责产品与业务
Unity 负责完整 AR 页面和数字人
EasyAR Mega 负责指定地点定位
AI 后端负责语音识别、知识问答和语音合成
```
第一步只验证“Flutter 打开现有 Unity AR，并继续看到定位后的绿色方块”。该验证通过后，再依次加入数字人和 AI 对话。不要同时改造 Flutter 集成、数字人、AI 和多地点配置，否则问题出现时无法快速判断故障边界。

