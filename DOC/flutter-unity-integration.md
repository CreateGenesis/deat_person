# Flutter Android 集成实现

## 范围

Flutter 工程位于 `flutter_app/`。本阶段将现有 Unity 能力作为库接入 Android 宿主，不重写 EasyAR、数字人或 M3 遮挡算法，也不引入第三方 Flutter-Unity 插件。

当前为单 Block 内部开发版本。M3 现场验收与 M4 集成开发分开推进；离线人物预览不启动相机和遮挡。

## 代码边界

| 路径 | 职责 |
| --- | --- |
| `flutter_app/lib/main.dart` | Flutter 首页与模式入口 |
| `flutter_app/lib/unity_bridge.dart` | Dart 方法通道、事件解析、超时和错误显示 |
| `flutter_app/android/app/src/main/kotlin/com/example/virtual_agent/MainActivity.kt` | Flutter 原生宿主、现场权限请求 |
| `flutter_app/android/app/src/main/kotlin/com/example/virtual_agent/UnityHostActivity.kt` | 全屏 Unity Activity、返回导航 |
| `flutter_app/android/app/src/main/kotlin/com/example/virtual_agent/UnityHostBridge.kt` | Ready 握手、待发 OpenPlace、JNI 回调、事件缓冲 |
| `Assets/Scripts/Host/UnityFlutterBridge.cs` | Unity 命令接收、状态回传、模式切换与开发控制面板 |
| `Assets/Scripts/EasyARCloudBlockBootstrap.cs` | 原现场流程及不依赖现场的预览分支 |
| `Assets/Editor/FlutterUnityExporter.cs` | Android 库导出和编辑器预览菜单 |

## 通信协议

- MethodChannel：`data_person/unity/methods`，提供 `getHostInfo` 与 `openAr`。
- EventChannel：`data_person/unity/events`，事件使用 JSON 字符串。
- Unity 接收对象名：`FlutterUnityBridge`，方法：`ReceiveCommand`。
- 所有消息必须包含 `version: 1`；请求带 `requestId` 时，直接响应回传它。自主状态通知没有 requestId。
- Flutter `openAr` 目前只传 `mode: preview|onsite`，现场使用现有场景配置，不接受任意后端地址、凭据或动态 Block 参数。

Unity 命令：

| type | 参数与行为 |
| --- | --- |
| OpenPlace | mode，首次初始化或重载场景切换模式 |
| PauseAR / ResumeAR | 停止/恢复 EasyAR Session，暂停/恢复 Unity 音频 |
| CloseAR | 先暂停资源，再回传 Closed，由 Android 前置 Flutter 页面 |
| SetConversationState | value 为现有会话状态枚举，仅开发状态切换，不会调用 AI 服务 |
| PlayGesture | value 为 Greeting / Yes / No / Positive |
| PreviewTracking | value 为 found / lost，仅允许预览模式 |

事件：Ready、Opened、Status、Paused、Resumed、Closed、GesturePlayed、Error。字段包含 type、requestId、mode、localization、conversation、message。

Flutter 首页只对外提供两种进入操作；人物调试命令由 Unity 页面控制面板发出。生命周期由 Unity 暂停回调和原生返回流程共同管理。

## 关闭策略

首版使用同一进程内保留 Unity Activity 的方式。关闭 AR 表示停止 Session、暂停音频、切回 Flutter，而不是销毁 Unity 运行时。这样避免 Unity 默认销毁流程结束整个应用进程，但不会释放全部 Unity 内存。

主页面返回键将整个任务置于后台，避免 Android 回退栈重新显示暂停中的 Unity 页面。Unity 返回操作等待 Closed 确认；超时会回到 Flutter 并记录错误，不能把超时视为资源释放验收通过。

## 验证边界

- Flutter 单元/Widget 测试覆盖入口呈现、OpenPlace 消息、重复打开保护、返回事件、权限失败、协议版本和就绪超时。
- 本次静态检查与 6 项 Flutter 测试通过；Unity 编辑器离线检查验证了人物复用、状态、手势、暂停恢复和重新打开，且 ARSession 数量为 0。报告见 `flutter_app/validation/unity-preview-check.json`。
- Unity 导出与 Android APK 构建分别验证；Unity 导出成功不代表最终安装包成功。
- 本机未连接 Android 手机时，无法声称已完成安装运行、10 次反复进入退出、锁屏恢复或相机释放验收。
- M3 模型导入警告、真实画面对齐和性能验收继续单独跟踪。
- 现有长期 EasyAR 凭据仍在 Unity 配置中，正式发布前必须移除长期服务凭据并完成短期 Token 改造。

操作步骤与设备检查清单见 `flutter_app/README.md`。
