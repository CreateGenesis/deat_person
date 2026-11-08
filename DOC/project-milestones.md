# EasyAR AI 数字人项目里程碑

## 文档信息

- 原始记录日期：2026-09-04
- 本次更新（2026-09-07）：完成 M5 客户端接口接入、Unity Library 导出和 ARM64 APK 构建；真实服务配置与联调保留。
- 当前产品形态：Flutter Android 调试 APK + Unity as a Library；保留 Unity 独立 APK 构建入口
- 最终目标：Flutter 主应用 + Unity as a Library + EasyAR Mega + AI 对话后端
- 当前所在位置：M3 现场验收待公司测试；M4 最小集成已实现，待 Android 真机生命周期验收；M5 客户端接口完成，待服务配置与真实对话验收
- 进度口径：原 70% / 32% 为历史粗估，不作为本次集成后的验收完成率。

## 总体路线

```text
M0 技术基线与构建
  -> M1 EasyAR 定位与空间放置
  -> M2 数字人基础表现
  -> M3 真实遮挡与 AR 视觉验收
  -> M4 Flutter + Unity 集成
  -> M5 AI 语音对话闭环
  -> M6 多地点与生产发布
```

## 里程碑总表

| 里程碑 | 状态 | 当前结论 |
| --- | --- | --- |
| M0 技术基线与 Android 构建 | 已完成 | Unity、URP、EasyAR、Mega、glTFast 已接入，Android ARM64 APK 构建成功 |
| M1 EasyAR 定位与空间放置 | 基本完成，待正式验收 | Block 定位、Found/Lost、数字人实例化和局部 Transform 已实现；用户已看到真机数字人，但缺少正式日志和稳定性记录 |
| M2 数字人基础表现 | 功能代码完成，待正式模型验收 | 头部注视、音量驱动口型和完整音频播放已接入；等待正式人物模型导入、面部绑定与真机调参 |
| M3 真实遮挡与 AR 视觉验收 | 原型代码完成，待推理与真机验收 | 遮挡原型已随 Unity 库导出；导出成功不等于模型推理、画面对齐和现场效果通过 |
| M4 Flutter + Unity 集成 | 最小集成已实现，待真机验收 | 已创建 flutter_app、导出 unityLibrary、接入全屏 Activity、JSON 桥接和离线预览；Flutter 测试与调试 APK 构建通过 |
| M5 AI 语音对话闭环 | 客户端接口完成，待联调 | REST/ticket/WS、文字增量、PCM 录音分帧、流式 TTS、播放回执、打断、心跳和重连已实现并进入 APK；尚未配置真实服务或完成真机对话 |
| M6 多地点与生产发布 | 未开始 | 仍是单 Block、测试包名、Development APK 和客户端长期凭据 |

## M0 技术基线与 Android 构建

状态：已完成。

已完成：

- Unity `6000.3.23f1` 与 URP `17.0.1`。
- EasyAR Sense `4003.1.0`、Mega `2.14.0`、Mega Studio `2.13.1`。
- glTFast `6.20.0` 与 RobotExpressive GLB 导入。
- Android ARM64、最低 API 25 构建配置。
- `Builds/EasyARCloudBlockTest-debug.apk` 构建成功。

验收结论：开发构建链路可用；正式签名、AAB 和发布配置归入 M6。

## M1 EasyAR 定位与空间放置

状态：基本完成，待正式验收。

已完成：

- 创建 EasyAR Mega Block Session。
- 编辑器使用 Simulator，Android 使用 Onsite。
- 监听 `TargetFound`、`TargetLost` 和定位响应。
- 定位成功后将 RobotExpressive 创建为 Block 子对象。
- 应用 Block 局部位置、旋转和缩放。
- 定位丢失时隐藏，恢复后复用原实例。

剩余验收：

- 保存 `Block_test_1` 真机定位成功日志。
- 从多个观察角度确认人物固定在真实空间。
- 验证位置、脚底高度、尺寸和朝向。
- 验证 Found/Lost 多次恢复无重复实例和状态异常。
- 记录定位时间、漂移、失败率和弱光表现。

完成标准：同一目标手机在现场连续完成 10 次定位与恢复测试，无明显漂移、重复实例或错误朝向。

## M2 数字人基础表现

状态：功能代码完成，等待正式人物模型配置与真机验收。

已完成：

- 定位状态与会话状态分离。
- Idle、Listening、Thinking、Speaking、Error 状态骨架。
- Wave、Yes、No、ThumbsUp 手势播放及状态返回。
- 自动识别 Humanoid 或常见命名的 Head 骨骼，并受限地注视 AR 相机。
- 播放完整 AudioClip 和 PCM16 缓冲。
- 使用回答语音振幅驱动嘴部 BlendShape；没有 BlendShape 时可配置 Jaw 骨骼回退。
- Android 触摸与编辑器键盘调试控制。

正式模型配置与验收：

- 已检查 `Traditional Chinese Hanfu dress` 的原始 FBX：Unity 导入后只有一个 `MeshFilter` 和 `135869` 个顶点，没有 `SkinnedMeshRenderer`、骨骼层级、BlendShape 或动画，当前只能静态显示，不能直接实现头部注视和口型。
- 若继续使用该模型，需要先在 Blender 等 DCC 工具中增加头部/下颌骨、蒙皮和嘴部 Shape Key；否则应更换为自带 Head 骨骼与嘴部 BlendShape 的模型。
- 导入正式人物模型，并在模型根节点按需添加 `AudioLipSyncController`。
- 优先绑定面部 `SkinnedMeshRenderer` 和 `mouthOpen`/`jawOpen` BlendShape；若模型没有口型 BlendShape，则绑定 Jaw 骨骼并调整张嘴旋转方向。
- 如果程序未自动识别头骨，在模型根节点的 `GazeController` 中手动绑定 Head。
- 新模型不需要动作时，将场景中的 `digitalHumanAnimatorController` 清空，避免继续套用 RobotExpressive 动画控制器。
- 根据真机语音音量调整静音阈值、说话阈值和最大张嘴权重。
- 记录移动端帧率、内存和温度。

完成标准：正式角色能持续注视用户，播放 AI 语音时口型同步开合，停止或打断后自然闭合；连续运行 30 分钟无明显掉帧或发热失控。

## M3 真实遮挡与 AR 视觉验收

状态：原型代码完成并随 Unity 库导出，等待模型推理验证与 Android 真机验收。

首版方案：保持 EasyAR 相机链路不变，使用 MediaPipe Selfie Segmenter 生成前景 Mask，并通过摄像机近裁剪面的深度写入网格在数字人绘制前建立遮挡关系。首版不引入 AR Foundation，也不依赖 URP Renderer Feature。

已完成：

- 从 EasyAR `CameraImageRenderer` 获取相机 RenderTexture。
- 接入兼容 Unity AI Inference 的 `selfie_segmenter.onnx`，输入输出均为 NHWC `256 x 256`；原始 TFLite 因自定义算子无法导入，已替换为同源 ONNX 转换模型。
- 使用 Unity AI Inference `GPUCompute` 执行，并提供 CPU 自动回退。
- 实现 Mask 阈值、1 像素边缘扩张和 attack/release 时域滤波，全程无 CPU 回读。
- 增加近裁剪面深度遮挡 Shader，使前景人体区域先于数字人写入深度。
- 增加启停、暂停恢复、资源释放和失败后自动关闭逻辑。
- 在场景中配置 15 FPS、0.55 阈值、1 像素扩张等首轮默认参数。

当前阻塞与剩余验收：

- 原许可证阻塞已不再阻止本次 Unity 库导出；模型已改用 ONNX，旧 `.tflite` 待导入描述不再适用。
- 运行 `Tools/Digital Human/Inspect Occlusion Model`，确认模型实际推理输出；已有导入警告仍需针对输出验证。
- 构建 Android APK，验证相机旋转、镜像、裁剪与 Mask 的屏幕坐标对齐；如上下颠倒，切换 `flipForegroundMaskVertically`。
- 真机校准阈值、边缘扩张、attack/release，并记录 15 FPS 推理时的总帧率、内存和温度。
- 增加 Mask 调试视图和性能统计，作为真机调参工具。

完成标准：手掌、手指和手臂横穿数字人时遮挡方向正确；快速移动时边缘无明显拖影；关闭遮挡后可立即回退；目标设备性能可接受。

后续升级：限定支持机型时，可评估 AR Foundation + ARCore Depth，实现真实环境深度与数字人深度比较。

## M4 Flutter + Unity 集成

状态：最小集成开发和调试 APK 构建完成，待 Android 真机验收。

已完成：

- 创建 `flutter_app/` Android 工程与 VS Code F5 调试配置。
- 增加 Unity 库导出菜单，复用原场景、模型、动画、EasyAR 与 M3 原型。
- 通过 Android 原生桥接打开全屏 Unity Activity，并回传版本 1 JSON 事件。
- 增加无需公司地点的离线人物预览，支持手势、会话状态、模拟 Found/Lost。
- 实现暂停、恢复、返回和再次进入逻辑；首版保留 Unity 运行时内存，不主动退出 Unity 进程。
- Flutter 静态检查、6 项测试及集成 Android 调试 APK 构建通过。

仍待验证与扩展：

- 尚未连接 Android 手机，需验证首次启动、权限拒绝、反复进出、锁屏恢复、相机释放和内存占用。
- OpenPlace 当前只选择 preview / onsite，现场仍使用固定 Block 配置；动态地点参数归入后续扩展。
- 离线预览不能代替真实相机、定位或 M3 遮挡验收。
- 详细操作与实现边界见 `flutter_app/README.md`、`DOC/flutter-unity-integration.md`。

原规划工作范围：

- 创建或接入 Flutter Android 主工程。
- 将 Unity 导出为 Unity as a Library。
- 使用全屏原生 Activity 承载 AR 页面。
- 实现版本化双向 JSON 消息。
- 支持 `OpenPlace`、`PauseAR`、`ResumeAR` 和 `CloseAR`。
- 回传 Session、定位、数字人和错误状态。
- 管理相机、音频、前后台和页面退出生命周期。

完成标准：Flutter 连续进入和退出 AR 页面 10 次，无黑屏、崩溃、相机占用残留或状态错乱。

## M5 AI 语音对话闭环

状态：客户端接口实现完成并通过离线协议检查与构建；未配置真实后端，未完成文字/语音联调和 Android 真机验收。

已确认：`virtual-agent-backend/` 提供 REST 建会话、一次性 ticket、`virtual-agent.v1` WebSocket 子协议、16 kHz 输入/24 kHz 输出 PCM16LE、24 字节二进制头，以及取消/播放回执/重连事件。接入方案见 `DOC/backend-integration/architecture.md`。

仍需输入：实际可访问的后端 Base URL、测试 realtime 授权、模型服务可用确认，以及地点/角色与知识库的映射需求。源码协议确认不等于服务已部署或模型调用成功。

已实现：

- REST 建会话、ticket 换取和文字增量回复处理。
- `WebSocketConversationTransport` 与后端事件/二进制协议适配。
- 麦克风权限、PCM16 采样转换和 20 ms 分帧。
- 心跳、超时、重连、取消和 session/turn/response/stream 关联。
- 接入后端 ASR、Agent 和 TTS；知识库内容与地点路由另行确认。
- 流式音频环形缓冲与播放。
- 会话状态驱动现有 Listening/Thinking/Speaking 表现。
- 支持用户打断正在播放的回答。

待配置与验收：

- 填入实际 HTTPS Base URL 与受限 realtime 测试授权，完成多轮文字联调。
- Android 真机验证麦克风、流式 TTS、VAD/手动打断、弱网重连和页面生命周期。
- 正式人物模型的面部绑定和口型参数调节归入 M2，不需要后端动作事件。

完成标准：完成连续多轮语音对话；弱网或服务错误不会卡死 AR 页面；回答可打断；日志能关联一次完整会话。

## M6 多地点与生产发布

状态：未开始。

主要工作：

- 将 Block ID、锚点、角色和对话配置数据化。
- 支持多地点、多个 anchorId 和资源版本。
- 轮换已经进入客户端的长期 EasyAR 凭据。
- 改为后端签发短期 Token，并确保日志脱敏。
- 统一正式 Android applicationId 与 EasyAR License。
- 完成正式签名、AAB、隐私权限和内容安全。
- 建立设备兼容矩阵、错误上报和发布回滚流程。
- 建立 Git 仓库、版本基线和最小自动化测试。

完成标准：使用正式配置完成目标机型回归，可以生成可发布 AAB，客户端不包含长期服务密钥。

## 当前执行顺序

1. 完成 M4 的非现场验证，连接 Android 手机检查离线预览与页面生命周期。
2. 继续 M2 人物表现与 M3 模型推理检查，不把离线预览计作遮挡验收。
3. 到公司后合并 M1、M2、M3，完成真实 AR 验收。
4. 配置测试后端，在离线人物预览中完成 M5 文字、流式 TTS、麦克风和打断联调。
5. 进入 M6 多地点配置、凭据安全和生产发布。

由于现场地点暂不可用，M3 现场验收独立保留，不阻塞 M5 非现场联调。后端客户端代码已进入 Flutter + Unity APK，但接口实现和构建通过不等于真实语音闭环验收。
