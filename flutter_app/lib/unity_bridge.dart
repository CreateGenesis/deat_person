import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';

enum UnityMode { preview, onsite }

class BackendLaunchConfig {
  static const environment = BackendLaunchConfig(
    baseUrl: String.fromEnvironment('VA_BACKEND_BASE_URL'),
    apiKey: String.fromEnvironment('VA_BACKEND_API_KEY'),
    site: String.fromEnvironment(
      'VA_BACKEND_SITE',
      defaultValue: 'Block_test_1',
    ),
  );

  const BackendLaunchConfig({
    required this.baseUrl,
    required this.apiKey,
    this.site = 'Block_test_1',
  });

  final String baseUrl;
  final String apiKey;
  final String site;

  bool get isConfigured =>
      baseUrl.trim().isNotEmpty && apiKey.trim().isNotEmpty;
}

class UnityBridge extends ChangeNotifier {
  static const methods = MethodChannel('data_person/unity/methods');
  static const eventChannel = EventChannel('data_person/unity/events');

  StreamSubscription<dynamic>? _subscription;
  Timer? _openTimeout;
  bool _disposed = false;
  bool available = false;
  bool isOpening = false;
  String status = '正在检查 Android 宿主';
  String localization = '';
  String conversation = '';
  String backend = '';
  String transcript = '';
  String response = '';
  String? error;
  final List<String> events = [];

  Future<void> initialize() async {
    if (kIsWeb || defaultTargetPlatform != TargetPlatform.android) {
      status = '此版本仅支持 Android ARM64 真机';
      _notify();
      return;
    }
    try {
      _subscription = eventChannel.receiveBroadcastStream().listen(
        _receive,
        onError: (Object failure) => _fail('事件通道异常：$failure'),
      );
      final info = await methods.invokeMapMethod<String, dynamic>(
        'getHostInfo',
      );
      if (_disposed) return;
      available = info?['unityAvailable'] == true;
      status = available ? '宿主就绪，可以打开 Unity' : '当前宿主不支持 Unity ARM64';
      _notify();
    } on PlatformException catch (failure) {
      _fail(failure.message ?? failure.code);
    } on MissingPluginException {
      _fail('未找到 Android 原生桥接，请在 Android 设备上完整重启应用。');
    }
  }

  Future<void> open(
    UnityMode mode, {
    BackendLaunchConfig? backendConfig,
  }) async {
    if (!available || isOpening || _disposed) return;
    isOpening = true;
    error = null;
    status = mode == UnityMode.preview ? '正在打开离线人物预览' : '正在打开公司现场 AR';
    _notify();
    _openTimeout?.cancel();
    _openTimeout = Timer(const Duration(seconds: 45), () {
      _fail('Unity 尚未回报就绪，请查看 Android 日志；可返回后重试。');
    });
    try {
      await methods.invokeMethod<void>('openAr', {
        'version': 1,
        'type': 'OpenPlace',
        'requestId': DateTime.now().microsecondsSinceEpoch.toString(),
        'mode': mode.name,
        'backendBaseUrl': backendConfig?.baseUrl.trim() ?? '',
        'backendApiKey': backendConfig?.apiKey.trim() ?? '',
        'site': backendConfig?.site.trim() ?? 'Block_test_1',
      });
    } on PlatformException catch (failure) {
      _fail(failure.message ?? failure.code);
    } on MissingPluginException {
      _fail('未找到 Unity 桥接，请重新构建 Android 应用。');
    }
  }

  void _receive(dynamic raw) {
    if (_disposed) return;
    try {
      final event = raw is String ? jsonDecode(raw) : raw;
      if (event is! Map || event['version'] != 1 || event['type'] is! String) {
        throw const FormatException('不支持的桥接事件');
      }
      final type = event['type'] as String;
      final message = event['message']?.toString() ?? '';
      localization = event['localization']?.toString() ?? localization;
      conversation = event['conversation']?.toString() ?? conversation;
      backend = event['backend']?.toString() ?? backend;
      transcript = event['transcript']?.toString() ?? transcript;
      response = event['response']?.toString() ?? response;
      events.insert(0, '$type${message.isEmpty ? '' : ' · $message'}');
      if (events.length > 30) events.removeLast();
      if (type == 'Opened' || type == 'Closed' || type == 'Error') {
        _openTimeout?.cancel();
        isOpening = false;
      }
      switch (type) {
        case 'Opened':
          status = event['mode'] == 'preview'
              ? '离线人物预览已打开'
              : '现场 AR 已打开，等待真实定位';
        case 'Closed':
          status = '已返回 Flutter，可再次进入 Unity';
        case 'Error':
          error = message.isEmpty ? 'Unity 返回错误，请查看 Android 日志。' : message;
        case 'BackendError':
          error = message.isEmpty ? 'AI 后端请求失败。' : message;
        case 'BackendStatus':
          if (backend == 'Ready') error = null;
        case 'Paused':
          status = 'Unity 会话已暂停';
        case 'Resumed':
          status = 'Unity 会话已恢复';
      }
      _notify();
    } on FormatException catch (failure) {
      _fail('无法解析 Unity 事件：${failure.message}');
    }
  }

  void clearEvents() {
    events.clear();
    _notify();
  }

  void _fail(String message) {
    if (_disposed) return;
    _openTimeout?.cancel();
    isOpening = false;
    error = message;
    _notify();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _openTimeout?.cancel();
    _subscription?.cancel();
    super.dispose();
  }
}
