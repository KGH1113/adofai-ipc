# ADOFAI-IPC v1 개발 가이드

[English](../en/000-DevelopGuide.md)

ADOFAI-IPC는 A Dance of Fire and Ice 모드가 함께 사용하는 로컬 WebSocket 게이트웨이입니다.
모드와 외부 앱은 서로 명령을 보내고, 이름 있는 결과 이벤트로 응답하고, 상태 변화를 발행합니다.
제품1.0.0의 wire protocol은3입니다. 기존 HTTP RPC API는 제거했습니다.

UnityModManager 모드 하나가 listener를 소유합니다. 각 모드는 namespace, 도메인 메시지 계약,
작업 수명과 상태를 소유하고 전송 계층은 라우팅과 연결 수명을 맡습니다.

1. [시작하기](001-GettingStarted.md)
2. [모드 API](002-ModApi.md)
3. [WebSocket 메시지 프로토콜](003-MessageProtocol.md)
4. [수명과 스레드](004-Lifecycle.md)
5. [TypeScript SDK](005-JsClient.md)
6. [의존성 Bootstrap](006-DependencyBootstrap.md)
7. [스트리밍 다운로드](007-Downloads.md)

애플리케이션은 자체 메시지·세션·스케줄러·시계·저장소 port에 의존하게 만드세요. SDK, Unity,
네이티브 구현은 composition root에서 주입합니다. 최초 snapshot과 revision을 가진 변경 이벤트로
상태를 투영하고, 상태를 반복 조회하는 polling은 제거합니다.
