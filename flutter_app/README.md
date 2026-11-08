# Flutter + Unity 数字人 Android 宿主

这个目录是独立 Flutter 工程。父目录仍是 Unity 源工程，不要把 Unity 的 Assets 复制到 Flutter 的 assets，也不要移动父目录中的场景、模型或 EasyAR 配置。

## 当前包含的功能

- Flutter 首页、离线人物预览入口、公司现场 AR 入口、桥接状态和事件列表。
- 同一 App 中的全屏 Unity Activity，保留 RobotExpressive、状态机、手势、注视、音频播放组件和 EasyAR 定位流程。
- 离线预览无需公司 Block、相机权限或网络；支持 Idle/Listening/Thinking/Speaking、手势以及模拟 Found/Lost。
- 现场入口继续使用 Unity 场景中的 Block_test_1、摆放参数和 M3 遮挡原型。
- 版本 1 JSON 双向桥接，支持进入、状态通知、暂停、恢复和返回。
- Virtual Agent 后端接口：REST 会话与换票、`virtual-agent.v1` WebSocket、文字增量、流式 PCM、录音、打断、心跳和重连。

离线人物预览不是 AR 测试：不会启动 EasyAR 相机、定位或 M3 遮挡。后端接口已实现，但未配置真实服务时不会联网；当前状态动画仍有复用 Idle 的情况。

## 在 VS Code 调试

1. 用 VS Code **单独打开本目录 flutter_app**，而不是只打开 Unity 根目录。
2. 安装推荐的 Flutter 和 Dart 扩展。
3. 连接 Android ARM64 手机，开启 USB 调试，并在手机上授权电脑。运行 `flutter devices` 应能看到该手机。
4. 第一次使用先执行 `flutter pub get`。如果 `unity_export/unityLibrary/build.gradle` 不存在，先完成下一节的 Unity 导出。
5. VS Code 右下角选择 Android 手机；不要选择 Windows、Edge 或 Chrome。
6. 在“运行和调试”中选择 `Android Flutter + Unity`，按 F5。
7. AI 后端连接保持留空可继续离线预览；联调时填写 HTTPS 服务地址、受限测试授权和地点标识，再打开 Unity 预览。
8. Unity 内可发送文字、开始麦克风、提交录音或打断回答；点“返回 Flutter”会关闭后端会话。

Flutter 页面支持 Dart 热重载。修改 Kotlin、AndroidManifest 或 Gradle 后，需要停止并完整重新运行；修改 Unity 的 C#、场景、模型或 Shader 后，需要重新导出 Unity 库，再完整运行 Flutter。

终端启动方式：

```powershell
flutter run -d <flutter devices 中的 Android 设备 ID>
```

构建调试安装包：

```powershell
flutter build apk --debug --target-platform android-arm64
```

APK 输出为 `build/app/outputs/flutter-apk/app-debug.apk`。

## 重新导出 Unity

1. 用 Unity 6000.3.23f1 打开父目录的 Unity 工程。
2. 退出 Play 模式，确认 C# 编译完成。
3. 选择 `Build > Export Unity Library for Flutter`。
4. 等待 `unity_export/export-summary.txt` 显示 `Result: Succeeded`。
5. 返回 VS Code，重新运行 Flutter。

也可以运行 VS Code 任务 `Unity: Export library`。它使用当前工程已有的 Unity Pipeline 本地桥接；若桥接不可用，仍可使用上面的菜单，不依赖任何云端服务。

`unity_export/` 是完整的 Unity Android 导出目录，Flutter 只引用其中的 `unityLibrary`，不会编译导出目录中的 `launcher`。`shared/`、`gradle.properties`、IL2CPP 源码和原生插件均需保留。不要手工修改生成内容，重新导出会覆盖它们。

导出期间临时选择经典 Unity Activity，结束后恢复原项目的 Android 入口设置；原独立 APK 构建菜单仍保留。不要同时运行独立 APK 构建和 Flutter 库导出。

## 没有手机时

在 Unity 选择 `Tools > Digital Human > Play Flutter Offline Preview`，可检查人物和 Unity 侧控制。退出时使用 Unity 的停止播放按钮。此模式不能验证 Android 原生桥接、Android 相机权限或 Activity 生命周期。

自动化检查不需要手机：

```powershell
flutter analyze
flutter test
```

本次 Flutter 静态检查与 8 项测试通过；Unity 后端协议校验、C# 编译、Unity Library 导出和 ARM64 IL2CPP APK 构建通过。已有离线人物检查结果保存于 `validation/unity-preview-check.json`，画面位于 `validation/unity-preview.png`。这些结果不包含后端真实服务或 Android 真机对话验收。

## 配置与生命周期边界

- 首版仅配置 Android ARM64、最低 API 25；AGP 9.0.1 / Gradle 9.1.0 与当前 Unity 导出配合使用。
- applicationId 保留 `com.example.virtual_agent`，以匹配当前 EasyAR License。不要只修改 Flutter 包名而不更新 License。
- 该包名与已有 Unity 独立测试 APK 相同，安装会尝试更新它；若签名不一致，会安装失败。不要为绕过错误自动卸载旧包，卸载可能删除其本地数据。
- “公司现场 AR”需要相机和精确位置权限；离线预览不请求这两项权限。首次点击 Unity 内 Mic 时由 Unity 请求录音权限。
- 后端地址、测试授权和地点只保存在当前 Flutter/Unity 进程内，不写入本地文件；不要把管理员密码或模型服务 AK 填入客户端。
- 返回 Flutter 时先停止 EasyAR Session、暂停音频，再将 Flutter Activity 前置。Unity Activity 和运行时保持在内存中，以便再次进入。
- 首版不调用 `Application.Quit()`、Unity Player `destroy()` 或主动卸载运行时来关闭 AR。返回页面不等于释放全部 Unity 内存；内存预算和重复进出仍需真机验收。
- 当前 Unity 工程原有长期 EasyAR 凭据未在这次集成中改造；它们会随客户端打包。此版本仅用于内部开发，不应公开分发，生产前必须完成凭据轮换和短期 Token 方案。

## 排错

- **找不到 Unity 库**：重新执行 Unity 导出菜单。
- **Gradle 下载超时**：检查 Gradle、Google Maven 和 Maven Central 的网络连通性；不需要升级本机 Flutter。
- **Android NDK 路径错误**：重新在当前电脑的 Unity 中导出，导出目录含本机 NDK 路径。
- **Unity 没有回报就绪**：保留 Flutter 事件记录，并使用 `flutter logs -d <设备 ID>` 或 Android SDK 的 `adb logcat` 检查 Unity / AndroidRuntime 日志。
- **现场无人物**：先确认位于 Block_test_1 的采集地点且定位成功；离线预览成功并不意味着现场定位成功。
- **M3 无遮挡**：目前仍属待验证原型，需另做模型推理、相机 Mask 对齐和目标手机性能验收。

## 首轮真机检查清单

- 连续 10 次：Flutter → 离线预览 → 返回 Flutter，无黑屏或进程退出。
- 离线模式拒绝相机权限仍能进入；现场模式拒绝权限给出明确提示。
- Unity 内切后台、锁屏、恢复；回到 Flutter 后确认相机隐私指示器停止。
- 预览 Found/Lost 恢复没有重复人物，手势结束后回到会话状态。
- 现场与预览互相切换，预览不保留 AR 相机背景或继续联网定位。
- 到公司后补真实定位、空间摆放和 M3 遮挡验收。
