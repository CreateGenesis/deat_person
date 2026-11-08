import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:virtual_agent/unity_bridge.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  final messenger =
      TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger;
  final calls = <MethodCall>[];

  setUp(() {
    calls.clear();
    messenger.setMockMethodCallHandler(UnityBridge.methods, (call) async {
      calls.add(call);
      if (call.method == 'getHostInfo') return {'unityAvailable': true};
      return null;
    });
    messenger.setMockMethodCallHandler(
      const MethodChannel('data_person/unity/events'),
      (call) async => null,
    );
  });

  tearDown(() {
    messenger.setMockMethodCallHandler(UnityBridge.methods, null);
    messenger.setMockMethodCallHandler(
      const MethodChannel('data_person/unity/events'),
      null,
    );
  });

  Future<void> emit(Map<String, Object> event) async {
    await messenger.handlePlatformMessage(
      'data_person/unity/events',
      const StandardMethodCodec().encodeSuccessEnvelope(jsonEncode(event)),
      (_) {},
    );
  }

  testWidgets('opens preview and consumes opened and closed events', (
    tester,
  ) async {
    final bridge = UnityBridge();
    addTearDown(bridge.dispose);
    await bridge.initialize();
    expect(bridge.available, isTrue);
    await bridge.open(UnityMode.preview);
    expect(bridge.isOpening, isTrue);
    expect((calls.last.arguments as Map)['mode'], 'preview');
    await emit({'version': 1, 'type': 'Opened', 'mode': 'preview'});
    await tester.pump();
    expect(bridge.isOpening, isFalse);
    expect(bridge.status, contains('预览已打开'));
    await emit({'version': 1, 'type': 'Closed'});
    await tester.pump();
    expect(bridge.status, contains('已返回 Flutter'));
  }, variant: TargetPlatformVariant.only(TargetPlatform.android));

  testWidgets('prevents duplicate open requests', (tester) async {
    final bridge = UnityBridge();
    addTearDown(bridge.dispose);
    await bridge.initialize();
    await bridge.open(UnityMode.preview);
    await bridge.open(UnityMode.onsite);
    expect(calls.where((call) => call.method == 'openAr'), hasLength(1));
    await emit({'version': 1, 'type': 'Opened', 'mode': 'preview'});
    await tester.pump();
  }, variant: TargetPlatformVariant.only(TargetPlatform.android));

  testWidgets('passes backend launch configuration without persistence', (
    tester,
  ) async {
    final bridge = UnityBridge();
    addTearDown(bridge.dispose);
    await bridge.initialize();
    await bridge.open(
      UnityMode.preview,
      backendConfig: const BackendLaunchConfig(
        baseUrl: 'https://agent.example.com',
        apiKey: 'temporary-key',
        site: 'Block_test_1',
      ),
    );
    final arguments = calls.last.arguments as Map;
    expect(arguments['backendBaseUrl'], 'https://agent.example.com');
    expect(arguments['backendApiKey'], 'temporary-key');
    expect(arguments['site'], 'Block_test_1');
    await emit({'version': 1, 'type': 'Opened', 'mode': 'preview'});
    await tester.pump();
  }, variant: TargetPlatformVariant.only(TargetPlatform.android));

  testWidgets('permission denial clears pending state', (tester) async {
    final bridge = UnityBridge();
    addTearDown(bridge.dispose);
    await bridge.initialize();
    messenger.setMockMethodCallHandler(UnityBridge.methods, (call) async {
      throw PlatformException(
        code: 'PERMISSION_DENIED',
        message: 'Camera denied',
      );
    });
    await bridge.open(UnityMode.onsite);
    expect(bridge.isOpening, isFalse);
    expect(bridge.error, 'Camera denied');
  }, variant: TargetPlatformVariant.only(TargetPlatform.android));

  testWidgets('rejects unsupported event versions', (tester) async {
    final bridge = UnityBridge();
    addTearDown(bridge.dispose);
    await bridge.initialize();
    await emit({'version': 2, 'type': 'Opened'});
    await tester.pump();
    expect(bridge.error, contains('无法解析'));
  }, variant: TargetPlatformVariant.only(TargetPlatform.android));

  testWidgets('surfaces backend state and transcript events', (tester) async {
    final bridge = UnityBridge();
    addTearDown(bridge.dispose);
    await bridge.initialize();
    await emit({
      'version': 1,
      'type': 'BackendStatus',
      'backend': 'Ready',
      'transcript': '你好',
      'response': '您好',
    });
    await tester.pump();
    expect(bridge.backend, 'Ready');
    expect(bridge.transcript, '你好');
    expect(bridge.response, '您好');
    expect(bridge.error, isNull);
  }, variant: TargetPlatformVariant.only(TargetPlatform.android));

  testWidgets('reports missing Unity readiness instead of spinning forever', (
    tester,
  ) async {
    final bridge = UnityBridge();
    addTearDown(bridge.dispose);
    await bridge.initialize();
    await bridge.open(UnityMode.preview);
    await tester.pump(const Duration(seconds: 46));
    expect(bridge.isOpening, isFalse);
    expect(bridge.error, contains('尚未回报就绪'));
  }, variant: TargetPlatformVariant.only(TargetPlatform.android));
}
