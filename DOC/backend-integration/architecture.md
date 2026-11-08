# 后端接入总览：Flutter + Unity + Virtual Agent Runtime

> Skill usage marker: used the project-teardown skill

- 分析日期：2026-09-06；客户端实施更新：2026-09-07。
- 后端仓库：`https://github.com/CreateGenesis/virtual-agent-backend.git`。
- 本地位置：工作区根目录下的 `virtual-agent-backend/`，与 `flutter_app/` 并列，保留独立 Git 仓库。
- 分析基线：`main` 分支，提交 `32f38e1`，`Initial commit: virtual agent backend and admin console`，提交时间 `2026-09-06T00:06:10+08:00`。
- 当前边界：已完成 Flutter/Unity 客户端接口实现、Unity Library 导出和 Android APK 构建；没有配置真实服务地址或授权，没有启动后端或调用模型服务。

## 1. 结论与系统分工

**可以接入现有项目，不需要把 Python 后端搬进 Flutter，也不需要重写数字人渲染。** 后端提供会话、语音识别、Agent 回复和语音合成；Flutter 提供业务入口；Unity 保留 EasyAR 定位、数字人表现和播放能力。

客户端现已实现 REST 换票、WebSocket 协议适配、录音分帧、流式播放和远端打断。M5 当前应记为“接口接入完成，待配置真实服务并联调”，不能记为“语音对话已验收”。

```text
Flutter：地点/角色入口、连接设置、权限说明、会话摘要
        │ 低频控制与状态事件（现有 Flutter ↔ Kotlin ↔ Unity 桥接）
        ▼
Unity：独立会话协调器 ── REST 建会话 / 获取一次性 ticket
        │
        ├── 麦克风 → 16 kHz PCM 分帧 ── WebSocket ──┐
        ├── 文本输入 / 取消 / 播放回执 ───────────────┤
        │                                           ▼
        │                                  FastAPI Session Actor
        │                                  VAD → MiMo ASR
        │                                  Deep Agents / DeepSeek
        │                                  MiMo TTS
        │                                           │
        └── 字幕、状态、24 kHz PCM ←─────────────────┘
                 ↓
        流式音频播放器 + 数字人状态机

EasyAR：继续独立管理定位与人物空间显示，不承担 AI 会话协议
```

后端代码体现的核心思路是：每个会话统一协调输入、回答、取消和播放反馈；模型生成结束与用户实际听完分开管理。后台 JobManager 负责长任务，不把所有操作都绑在一次前台语音回答上。

### 已确认、建议与未知

| 类别 | 结论 |
| --- | --- |
| 已确认 | 后端采用 FastAPI、Deep Agents/LangGraph、DeepSeek、MiMo，提供 REST 和带 ticket 的 WebSocket；`web/` 是 React 调试管理台，不是数字人渲染端。 |
| 已确认 | 当前 Flutter 使用全屏 `UnityHostActivity`；Unity 有状态机、手势、完整 PCM 播放和内部 JSON 桥接。 |
| 接入建议 | 首版让 Unity 单独拥有 AR 页的会话连接、录音和播放，Flutter 只传递低频指令与显示结果；避免把每 20 ms 音频经多层 JSON/base64 转发。 |
| 尚未验证 | 同事是否已部署可访问服务、真实模型调用是否成功、设备端音频延迟和回声表现、公司现场 AR 效果。 |
| 尚未确认 | 地点/角色如何选择提示词、知识库或工具；仓库没有明确的数字人口型、表情、语义手势事件协议。 |

## 2. 客户端实施状态

| 入口 | 当前实现 | 仍需验证 |
| --- | --- | --- |
| `Assets/Scripts/DigitalHuman/Conversation/BackendSessionClient.cs` | REST 创建/关闭会话、重连换票、Bearer 鉴权、相对 WS URL 转换和音频格式校验。 | 真实 HTTPS 地址、401/403/409/503 响应和 ticket 过期。 |
| `Assets/Scripts/DigitalHuman/Conversation/WebSocketConversationTransport.cs` | `virtual-agent.v1` 子协议、文本/二进制消息、分片合并、串行发送和关闭通知。 | Android 真机上的 TLS、弱网关闭码和网络切换。 |
| `Assets/Scripts/DigitalHuman/Conversation/RealtimeProtocol.cs` | 后端 envelope、24 字节音频头、RFC 4122 UUID 顺序、大端序号以及播放回执/取消事件。 | 与部署版本的协议兼容性。 |
| `Assets/Scripts/DigitalHuman/Conversation/RealtimeConversationSession.cs` | 会话状态、序号去重、心跳、三次退避重连、文字增量、流式 TTS、打断和实际播放回执。 | 多轮真实回复、重连 replay、服务端 VAD 打断和长回答缓冲。 |
| `Assets/Scripts/DigitalHuman/Conversation/MicrophonePcmCapture.cs` | Android 运行时录音授权、设备采样、单声道混合、16 kHz 重采样、20 ms/640 字节分帧和尾帧补齐。 | 不同手机采样率、回声、免提全双工和权限拒绝恢复。 |
| `Assets/Scripts/DigitalHuman/SpeechAudioPlayer.cs` | 原完整音频播放保留，新增 12 秒有界环形缓冲、180 ms 预缓冲、流式结束和播放时长。 | 目标设备音频延迟、欠载和空间音频效果。 |
| `Assets/Scripts/Host/UnityFlutterBridge.cs` | 独立持有会话组件；提供连接、文字、麦克风、提交和打断控件，回传状态/字幕。 | 真机页面退出、后台恢复及连续进入退出。 |
| `flutter_app/lib/main.dart` / `flutter_app/lib/unity_bridge.dart` | 地址、测试授权和地点为启动时输入，只经桥接传递，不落盘；留空保持离线模式。 | 正式用户鉴权与短期票据代理。 |

会话组件挂在持久化的 `FlutterUnityBridge` 对象上，不挂在 TargetLost 时会停用的人物对象上；人物重载后重新绑定播放器和状态机。

**全屏边界：** 当前不是把 Unity 作为 Flutter Widget 嵌入。首版实时字幕和输入/取消按钮可放在 Unity 离线预览/AR 调试面板；若要 Flutter 风格控件覆盖 AR，需要另做 UI 集成，不能默认现有 Flutter 页面会盖在 Unity Activity 上。

若改为 Flutter 持有整条音频链，需要额外验证全屏 Unity 时 Flutter Activity 后台生命周期、音频插件与 Unity 播放之间的数据通道；本轮不优先选该路线。Unity 源码修改后需重新导出 `unityLibrary`，不以直接修改 `flutter_app/unity_export/` 的生成文件作为长期方案。

## 3. 首条对话的连接流程

### 3.1 REST 建会话，再使用一次性 ticket 连接

1. 配置实际后端 Base URL。GitHub 仓库地址不是服务地址。
2. 请求 `GET /api/v1/health/live` 和 `GET /api/v1/ready`。后者只检查数据库和模型配置齐全，不证明真实 LLM/ASR/TTS 请求成功。
3. 以 `Authorization: Bearer <测试 API Key>` 调用 `POST /api/v1/sessions`；API Key 至少需要 `realtime` scope，客户端首版保持 `debug:false`。

```json
{
  "client_reference": "flutter-android",
  "metadata": {"site": "Block_test_1"},
  "debug": false
}
```

4. 保存响应中的 `session_id`、`expires_at`、`subprotocol` 和输入/输出音频描述，立刻使用 `websocket_url` 连接。
5. 源码返回的是相对路径 `/api/v1/realtime?ticket=...`，需相对后端地址补全主机，并将 HTTPS/HTTP 转为 WSS/WS；显式提供子协议 `virtual-agent.v1`。
6. ticket 默认有效期 30 秒且只能用一次，不缓存复用、不写日志。收到 `session.snapshot`、历史重放和 `session.ready` 后进入可交互状态，发送 `client.ready`。

`metadata.site` 在这里仅是关联标记。后端创建会话时存储 metadata 并读取已发布配置，不代表已经根据地点自动选择角色或知识库。

### 3.2 控制事件的最小合同

```json
{
  "v": 1,
  "type": "input.text",
  "event_id": "<客户端生成的 UUID>",
  "session_id": "<服务端返回的 session_id>",
  "turn_id": "<本轮 UUID>",
  "data": {"text": "你好，请介绍一下这里"}
}
```

后端事件统一包含 `v/type/seq/event_id/session_id/ts/data`，并按需携带 `turn_id/response_id/stream_id` 相关信息；音频流 ID 位于音频事件的 `data.stream_id`。

| 事件 | 客户端处理 |
| --- | --- |
| `input.transcript.started/delta/completed` | 展示识别进度及结果，不把转写文本再次作为新 input.text 发送。 |
| `response.started` / `response.text.delta` | 关联 `turn_id/response_id`；从 `data.delta` 追加回答，不重复显示完整文本。 |
| `response.audio.started` | 从 `data.stream_id/format/sample_rate/channels` 建立流，并关联顶层 `response_id`。 |
| `response.audio.completed` / 二进制 END | 表示生成端不会继续发送该音频流；仍等待本地已排队音频播放完。 |
| `response.completed` | 表示服务端回答结束，不直接将正在播放的人物切回 Idle。 |
| `response.cancelled` / `response.audio.stopped` | 停止对应播放、清空对应缓冲、丢弃该 response/stream 的残留包。 |
| `error` / `pong` / `job.*` | 分别处理错误、心跳和后台任务状态；后台任务不是当前前台回答。 |

实际开始/进度/完成/停止播放时发送 `playback.ack`，例如：

```json
{
  "v": 1,
  "type": "playback.ack",
  "event_id": "<UUID>",
  "data": {
    "state": "started",
    "response_id": "<response_id>",
    "stream_id": "<stream_id>",
    "played_ms": 0
  }
}
```

`state` 支持 `started/progress/completed/stopped`。播放时长来自本地真实消费量，不是接收字节数。建议按实际开播进入 Speaking、缓冲耗尽后退出 Speaking；用户打断还必须发送 `response.cancel`，不能只调用本地 `Interrupt()`。

### 3.3 音频不是裸 PCM：每条消息包含 24 字节头

| 部分 | 格式 |
| --- | --- |
| 字节 0–3 | `version=1`、`kind`、`flags`、`reserved=0`，各 1 字节。输入 kind=1，输出 kind=2；START=1，END=2。 |
| 字节 4–7 | `chunk_seq`，32 位无符号大端整数。 |
| 字节 8–23 | `stream_id` 的 UUID 原始 16 字节，顺序与 Python `UUID.bytes` 一致。 |
| 字节 24 起 | PCM16LE payload，音频采样本身是小端。 |
| 上行输入 | 16 kHz、单声道、每帧 20 ms，payload 必须恰好 640 字节，含头共 664 字节。 |
| 下行输出 | 24 kHz、单声道、常规每帧 100 ms，payload 4800 字节；末帧允许更短。 |

必须处理的边界：首个输入包设置 START 且序号为 0，后续同流序号连续；`audio.start` JSON 不代替二进制 START。设备录音不能假定原生就是 16 kHz，需要检查采样结果并重采样/分帧；输入短尾补齐成有效帧后再结束，不能直接发送短 payload。

输入可用二进制 END 或 `audio.commit` 提交；`audio.stop` 默认是丢弃未提交输入，只有 `data.commit:true` 才提交。持续语音由服务端 VAD 分段；ASR 实现将分段 PCM 封装成 WAV 后提交，不能仅因有 delta 事件就宣称逐字实时 ASR 已验收。

输出需要接受只有 24 字节头、payload 为空的 END，以及带短尾数据的 END。不要把这种合法结束包当无效 PCM。录音同时播放时的回声/自触发打断尚未验证，首版先按键说话或耳机联调，再验收免提全双工。

### 3.4 断线、取消和页面退出

- 记录已实际消费的事件序号；重连调用 `POST /api/v1/sessions/{session_id}/tickets`，body 为 `{"last_event_seq":123}`，取得新 ticket 再连接。同一会话受原 API Key 权限约束，关闭/过期会话不能恢复。
- 服务端重放 JSON 事件，不重放音频。按序号/事件 ID 去重；不要在消费 replay 前把 snapshot 的 `latest_event_seq` 直接视为所有历史均已处理，也不要盲目重发文字输入。
- 区分协议拒绝：4406 是子协议缺失，4401 是 ticket 无效或过期，4429 是连接容量限制；不要无限快速重连。
- 用户打断先本地停播并清队列，再发送 `response.cancel` 和真实的 stopped 回执；VAD 触发的远端打断也走同一清理路径。前台取消不等于取消后台 job。
- 建议首版进后台/退出 AR 时停止录音和播放；退出会话调用 `DELETE /api/v1/sessions/{session_id}` 并关闭 WS。恢复页面若会话已关闭则创建新会话，不复用旧 ticket。

## 4. 分阶段推进与验收

| 阶段 | 工作 | 最小验收 |
| --- | --- | --- |
| M5-A 文字链路 | 客户端实现完成，待配置联调。 | 在不需要 EasyAR 定位的预览页完成多轮文字对话，可关联 session/turn/response；有明确的正常关闭路径。 |
| M5-B 流式 TTS | 客户端实现与离线协议检查完成，待真实音频联调。 | 不整段等待、不逐包重启 Clip；短尾/空 END 正常；播放结束才退出 Speaking。 |
| M5-C 麦克风与打断 | 客户端实现完成，待 Android 真机和服务联调。 | 按键语音可以问答；权限拒绝可恢复；打断后无残留音频；再测试 VAD 与免提回声。 |
| M5-D 异常与生命周期 | 心跳、重连、退出清理已实现；弱网和真机生命周期待验收。 | 弱网不死循环；重新进入无重复会话/播放；定位丢失不造成隐式资源泄漏。 |

M5-A/B/C 的客户端代码已经进入 APK；真实联调仍需要服务地址与授权。麦克风、扬声器和 Activity 生命周期验收仍需 Android 真机。M3 的公司现场定位与遮挡验收独立保留。

口型、表情和语义手势不是本次后端自动提供的效果。首版先复用 Listening/Thinking/Speaking 等表现，后续再约定动作事件或增加本地音频驱动口型。不要把公司知识库已接通作为 M5-A 的默认前提。

## 5. 本机运行条件与凭据边界

仓库 `pyproject.toml:6` 限定 Python `>=3.13,<3.14`。本次实际执行的 Python 为 3.11.14；`uv python list --only-installed` 检测到 3.11、3.12、3.14，没有 3.13。不能直接向现有共享 FastAPI 环境安装此项目。

如果同事已有部署，优先联调其测试环境，不必先本机搭全套。若之后需要本地服务，可在后端目录使用独立 uv 环境；以下是后续步骤，本次未执行：

```powershell
Set-Location 'virtual-agent-backend'
uv python install 3.13
uv sync --all-groups --frozen
Copy-Item -LiteralPath '.env.example' -Destination '.env'
npm --prefix web ci
npm --prefix web run build
```

复制配置前确认 `.env` 不存在，避免覆盖。然后配置开发模式、管理员密码和独立随机密钥，并按仓库说明准备数据目录；模型服务的 Agent/ASR/TTS Base URL、模型和 AK 由管理台或服务端环境变量配置。再运行 `uv run uvicorn backend.app.main:app --host 127.0.0.1 --port 8000 --reload`。本轮没有验证完整后端在 Windows 上启动，仓库生产模板面向 Linux/systemd/Nginx，且明确要求单 worker。

本机浏览器/Unity Editor 可以访问电脑回环地址，Android 真机上的 `127.0.0.1` 不是电脑。真机优先使用可访问的 HTTPS 测试域名；若改用局域网调试，还需单独处理监听地址、防火墙和 Android 调试网络配置，不在发布包里全局放开明文流量。只有受隔离的本地 npm MCP 执行需要 rootless Docker，远程 MCP 不依赖 Docker；首条文字/语音链不以接入 MCP 为前提。

**安全设计建议：** 开发期只用受限、可撤销的测试 realtime API Key，运行时输入、内存保存，配置/桥接/网络日志都要脱敏。不要把管理员密码、DeepSeek/MiMo AK 或长期 realtime API Key 写进 Dart、C#、APK、示例文件或聊天。

生产应由可信服务鉴别登录用户并代为签发/换取短期 ticket，手机只接触短期凭证；用户到会话的归属校验和票据代理是待设计项，不能因为仓库已有 ticket 就认为手机用户鉴权已完成。

## 6. 需要后端同事补充的输入

1. 实际可访问的测试 Base URL，HTTPS/WSS 是否已部署，是否需要 VPN 或同一网络。
2. 带 `realtime` scope 的测试凭据安全交付方式，或现成的业务端 ticket 签发接口；无需把密钥贴进聊天。
3. Agent、ASR、TTS 是否已配置，管理台真实文字/语音请求是否通过；`/ready` 为 200 还不足以替代此项。
4. 当前人物的角色提示词、公司资料来源，以及 Block/地点与角色/知识库的映射需求。
5. 开发和生产的会话归属、重连、后台任务退出策略，以及有无设备端延迟/并发目标。

## 7. 本次验证与源码导航

- 已完成真正的 Git clone，保留 origin 和版本历史；未修改后端源码。
- 用 Python 3.11.14 的标准库隔离导入 `realtime/protocol.py`，12 项检查通过：常量大小、输入往返、UUID 顺序、大端序号、短输入拒绝、reserved/flags 校验、短头拒绝、输出短尾、PCM 对齐、空 END、控制事件版本/解析等。
- Unity 客户端离线校验通过：音频头、UUID 顺序、空 END、JSON envelope、48 kHz→16 kHz 分帧和 ticket URL 转换；Unity C# 编译为 0 错误。
- Flutter `analyze` 无问题，8 项测试通过；Unity Library 重新导出成功，ARM64 IL2CPP 调试 APK 构建成功。
- APK：`flutter_app/build/app/outputs/flutter-apk/app-debug.apk`，SHA256 `A544C54D3C3985793396C5B8678D8651062CA70FAB035CBFB3667CABF29D661D`。
- 上述检查不等价于 Python 3.13 后端运行、模型接口或 Android 真机真实对话通过。本次未安装后端依赖、未创建 `.env`、未启动服务、未调用收费模型。

| 主题 | 源码入口，路径相对工作区根目录 |
| --- | --- |
| REST 与 ticket | `virtual-agent-backend/backend/app/api/public.py:29`、`virtual-agent-backend/backend/app/schemas.py:603` |
| WS 鉴权和子协议 | `virtual-agent-backend/backend/app/api/websocket.py:67` |
| 音频与 envelope | `virtual-agent-backend/backend/app/realtime/protocol.py:11` |
| 输入、取消、播放反馈 | `virtual-agent-backend/backend/app/realtime/session.py:329` |
| 输出流及空 END | `virtual-agent-backend/backend/app/realtime/session.py:954` |
| ASR 分段请求 | `virtual-agent-backend/backend/app/providers/mimo.py:163` |
| 健康检查边界 | `virtual-agent-backend/backend/app/main.py:174` |
| 浏览器参考客户端 | `virtual-agent-backend/web/src/hooks/useRealtime.ts:1`、`virtual-agent-backend/web/src/lib/audio.ts:1` |
| 运行与发布限制 | `virtual-agent-backend/README.md:33`、`virtual-agent-backend/pyproject.toml:6` |

## 8. 后续专题拆解

1. 优先：填入测试地址与 realtime 授权，完成 M5-A 多轮文字联调。
2. 其次：Android 真机验证流式播放、录音、VAD/手动打断及回声处理。
3. 最后：按地点/角色选择配置，以及生产用户鉴权和短期票据分发。
