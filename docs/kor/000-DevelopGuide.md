# ADOFAI-IPC v2 개발 가이드

[English](../en/000-DevelopGuide.md)

ADOFAI-IPC 2.0.0은 게임 프로세스가 소유하는 로컬 WebSocket 게이트웨이입니다.
소비 모드는 DLL을 동봉하고 기능을 등록합니다. 비활성화 시 자기 등록만 Dispose합니다.
처음 호출한 모드도 서버를 소유하지 않습니다. 호스트는 게임 종료 또는 application unload까지 유지합니다.
런타임에는 UnityModManager·JALib·자동설치·업데이트·launcher·migration 의존성이 없습니다.

제품 버전 2.0.0, 계약 major 1(어셈블리 식별 버전 1.0.0.0), wire major 3,
각 기능의 메시지 protocol major는 별도로 관리합니다.

1. [시작하기](001-GettingStarted.md)
2. [모드 API](002-ModApi.md)
3. [WebSocket 메시지 프로토콜](003-MessageProtocol.md)
4. [수명과 스레드](004-Lifecycle.md)
5. [TypeScript SDK](005-JsClient.md)
6. [동봉 런타임과 버전 계약](006-BundledRuntime.md)
7. [스트리밍 다운로드](007-Downloads.md)

모드의 핵심 기능은 IPC에 의존하지 않고, 진입점에서 IPC 어댑터를 조립합니다.
