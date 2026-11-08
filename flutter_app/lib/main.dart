import 'package:flutter/material.dart';

import 'unity_bridge.dart';

void main() => runApp(const DigitalHumanApp());

class DigitalHumanApp extends StatelessWidget {
  const DigitalHumanApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: '数字人 AR 工作台',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: const Color(0xFF4355B9)),
        scaffoldBackgroundColor: const Color(0xFFF4F5FA),
        useMaterial3: true,
      ),
      home: const HomePage(),
    );
  }
}

class HomePage extends StatefulWidget {
  const HomePage({super.key});

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  late final UnityBridge bridge;
  final backendUrl = TextEditingController(
    text: BackendLaunchConfig.environment.baseUrl,
  );
  final backendApiKey = TextEditingController(
    text: BackendLaunchConfig.environment.apiKey,
  );
  final backendSite = TextEditingController(
    text: BackendLaunchConfig.environment.site,
  );

  @override
  void initState() {
    super.initState();
    bridge = UnityBridge()..initialize();
  }

  @override
  void dispose() {
    backendUrl.dispose();
    backendApiKey.dispose();
    backendSite.dispose();
    bridge.dispose();
    super.dispose();
  }

  BackendLaunchConfig get backendConfig => BackendLaunchConfig(
    baseUrl: backendUrl.text,
    apiKey: backendApiKey.text,
    site: backendSite.text,
  );

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: bridge,
      builder: (context, child) => Scaffold(
        appBar: AppBar(title: const Text('数字人 AR 工作台')),
        body: SafeArea(
          child: ListView(
            padding: const EdgeInsets.all(20),
            children: [
              Text(
                '让数字人走进现实',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const SizedBox(height: 8),
              const Text('Flutter 应用入口 · Unity 数字人 · EasyAR 空间定位'),
              const SizedBox(height: 20),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(18),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '运行状态',
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      const SizedBox(height: 12),
                      Text(bridge.status),
                      if (bridge.localization.isNotEmpty)
                        Text(
                          '定位：${bridge.localization}  会话：${bridge.conversation}',
                        ),
                      if (bridge.backend.isNotEmpty)
                        Text('AI 后端：${bridge.backend}'),
                      if (bridge.isOpening) ...[
                        const SizedBox(height: 12),
                        const LinearProgressIndicator(),
                      ],
                      if (bridge.error != null) ...[
                        const SizedBox(height: 12),
                        SelectableText(
                          bridge.error!,
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 12),
              ExpansionTile(
                leading: const Icon(Icons.cloud_outlined),
                title: const Text('AI 后端连接'),
                tilePadding: EdgeInsets.zero,
                childrenPadding: const EdgeInsets.only(bottom: 12),
                children: [
                  TextField(
                    controller: backendUrl,
                    keyboardType: TextInputType.url,
                    autocorrect: false,
                    decoration: const InputDecoration(
                      labelText: '服务器地址',
                      hintText: 'https://agent.example.com',
                      prefixIcon: Icon(Icons.link),
                    ),
                  ),
                  const SizedBox(height: 10),
                  TextField(
                    controller: backendApiKey,
                    obscureText: true,
                    autocorrect: false,
                    enableSuggestions: false,
                    decoration: const InputDecoration(
                      labelText: '测试授权',
                      prefixIcon: Icon(Icons.key_outlined),
                    ),
                  ),
                  const SizedBox(height: 10),
                  TextField(
                    controller: backendSite,
                    autocorrect: false,
                    decoration: const InputDecoration(
                      labelText: '地点标识',
                      prefixIcon: Icon(Icons.place_outlined),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              _EntryCard(
                title: '离线人物预览',
                description: '无需公司地点、相机权限或云端定位。检查人物、手势、状态切换和返回流程。',
                icon: Icons.view_in_ar,
                buttonText: '打开 Unity 预览',
                onPressed: bridge.available && !bridge.isOpening
                    ? () => bridge.open(
                        UnityMode.preview,
                        backendConfig: backendConfig,
                      )
                    : null,
              ),
              const SizedBox(height: 12),
              _EntryCard(
                title: '公司现场 AR',
                description: '使用当前 Block_test_1 配置。需要到采集地点，并允许相机与位置权限。',
                icon: Icons.location_on_outlined,
                buttonText: '进入 EasyAR 现场模式',
                onPressed: bridge.available && !bridge.isOpening
                    ? () => bridge.open(
                        UnityMode.onsite,
                        backendConfig: backendConfig,
                      )
                    : null,
              ),
              const SizedBox(height: 18),
              const Text('验证边界', style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 6),
              const Text(
                '预览模式不启动 EasyAR 相机或 M3 分割遮挡，不代表现场定位与遮挡验收通过。AI 接口已接入，实际服务配置与联调验收保留。',
              ),
              const SizedBox(height: 20),
              Row(
                children: [
                  const Expanded(
                    child: Text(
                      '桥接事件',
                      style: TextStyle(fontWeight: FontWeight.w700),
                    ),
                  ),
                  TextButton(
                    onPressed: bridge.clearEvents,
                    child: const Text('清空'),
                  ),
                ],
              ),
              if (bridge.events.isEmpty)
                const Text('打开 Unity 后，这里会记录进入、定位、会话和返回事件。'),
              for (final event in bridge.events)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Text(
                    event,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _EntryCard extends StatelessWidget {
  const _EntryCard({
    required this.title,
    required this.description,
    required this.icon,
    required this.buttonText,
    required this.onPressed,
  });

  final String title;
  final String description;
  final IconData icon;
  final String buttonText;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(icon),
                const SizedBox(width: 10),
                Text(title, style: Theme.of(context).textTheme.titleMedium),
              ],
            ),
            const SizedBox(height: 10),
            Text(description),
            const SizedBox(height: 14),
            FilledButton(onPressed: onPressed, child: Text(buttonText)),
          ],
        ),
      ),
    );
  }
}
