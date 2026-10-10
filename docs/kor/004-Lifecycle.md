# 수명과 스레드

[목차](000-DevelopGuide.md) | [English](../en/004-Lifecycle.md)

프로세스 호스트가 persistent dispatcher 하나와 WebSocket 서버 하나를 소유합니다.
소비 모드는 자기 기능 등록과 생성한 local peer만 소유합니다.
처음 호출한 모드를 끄거나 모든 등록을 해제해도 호스트는 유지합니다.
Unity quitting/unloading 때 호스트를 종료합니다.

configure에서 handler와 listener를 설치하고 도메인 상태 준비 후 MarkReady합니다.
MarkInitializing으로 재초기화, MarkError로 실패를 알립니다.
ready 전환과 재연결 때 현재 snapshot을 보내고 변경 명령은 재전송하지 않습니다.

명령은 Background 또는 GameThread를 선택합니다. async 이후 continuation이 게임 스레드를
유지한다고 가정하지 마세요. Unity 접근 시 runtime.RunOnGameThread를 사용합니다.
기능 lifecycle/event 콜백은 게임 스레드, local peer Message는 worker에서 실행합니다.
Dispose나 취소 콜백에서 게임 스레드를 기다리며 block하지 마세요.
dispatcher는 최대 1024개 대기 작업과 프레임당 64개 실행으로 제한합니다.

기능 Dispose는 대기 명령을 즉시 취소하며 나중에 큐가 실행돼도 취소한 명령은 실행되지 않습니다.
실행 중인 명령은 실제 종료까지 공간을 유지하므로 CancellationToken을 직접 확인합니다.
미사용 다운로드 티켓도 폐기합니다. 모드가 만든 local peer는 비활성화 때 직접 Dispose합니다.
공용 호스트가 계속 살아 있다고 소비 모드의 peer를 남겨 두지 마세요.

도메인 revision·소유권·lease·timeout·복구 정책은 애플리케이션이 책임집니다.
파일·네트워크·압축·긴 작업은 게임 스레드에서 수행하지 않습니다.
