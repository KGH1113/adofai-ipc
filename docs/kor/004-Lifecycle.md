# 수명과 스레드

[목차](000-DevelopGuide.md) | [English](../en/004-Lifecycle.md)

initializing 중 handler와 peer listener를 등록하고 데이터를 준비한 뒤 MarkReady하세요.
초기화 실패는 MarkError(code,message), 재초기화 시작은 MarkInitializing으로 표시합니다.
ready 전에 명령은 실행되지 않습니다. 비활성화 시 UnregisterNamespace는 명령 token과 티켓을
취소하고 unavailable을 알리며 소유 peer 정리를 유도합니다.

PeerSubscribed는 ready 수명 snapshot 뒤 실행됩니다. 모드가 없거나 초기화 중일 때 먼저 구독한
peer도 MarkReady에서 hydrate합니다. client도 연결·ready 대기 전에 snapshot/event/command
listener를 설치하세요. snapshot과 delta의 revision은 각 도메인이 책임집니다.

일반 handler와 PeerSubscribed/PeerDisconnected/PeerEvent는 Unity main thread 밖에서
실행될 수 있습니다. 이벤트에서 Unity를 만질 때는 주입한 dispatcher를 사용하세요.
RegisterMainThreadCommand만 자동으로 게임 스레드를 선택합니다. 그 안에서는 짧은 작업만
실행하고 다운로드·압축·네트워크·대기는 background로 옮겨 frame을 막지 마세요.

재연결은 peer/connection ID를 바꾸고 구독을 복원하며 새 snapshot을 받습니다. 명령은 자동
재전송하지 않습니다. 기존 작업의 실패·취소·복구는 application session에서 처리합니다.
wire mismatch는 재시도를 멈춥니다. 최초 서버 부재는 UNAVAILABLE이므로 앱 supervisor에서
초기 연결 재시도 정책을 구현하세요.

렌더링 같은 작업의 lease, virtual clock, input gate, frame ACK는 전송 heartbeat와 별도로
유지합니다. 파일 소비가 끝나야 다음 frame을 만들 수 있다면 in-flight1을 지키세요.
UI 프로세스가 이미 종료돼도 close/dispose에서 gate와 파일·자원을 정리해야 합니다.
