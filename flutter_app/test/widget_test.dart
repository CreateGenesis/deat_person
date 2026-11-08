import 'package:flutter/foundation.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:virtual_agent/main.dart';

void main() {
  testWidgets('shows both entry points without pretending AR is verified', (
    tester,
  ) async {
    await tester.pumpWidget(const DigitalHumanApp());
    await tester.pump();
    expect(find.text('数字人 AR 工作台'), findsOneWidget);
    expect(find.text('离线人物预览'), findsOneWidget);
    expect(find.text('公司现场 AR'), findsOneWidget);
    expect(find.text('AI 后端连接'), findsOneWidget);
    expect(find.textContaining('此版本仅支持 Android'), findsOneWidget);
  }, variant: TargetPlatformVariant.only(TargetPlatform.linux));
}
