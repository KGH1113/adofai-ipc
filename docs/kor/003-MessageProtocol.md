# WebSocket 메시지 프로토콜

[목차](000-DevelopGuide.md) | [English](../en/003-MessageProtocol.md)

주소는 `ws://127.0.0.1:32145/ipc/ws`이며32155까지 대체 포트를 사용합니다.
subprotocol은 `adofai-ipc.v3`, 제품 버전1.0.0, wire version3입니다. SDK와 런타임은 wire version을
검증하며 각 namespace의 도메인 protocol version은 payload에서 별도로 협상합니다.

UTF-8 JSON text message 하나가 제어 메시지 하나입니다.

```json
{"kind":"command","namespace":"example-mod","name":"level.open","id":"c1","payload":{"levelId":"abc"}}
```

```json
{"kind":"event","namespace":"example-mod","name":"level.opened","id":"e1","correlationId":"c1","payload":{"levelId":"abc"}}
```

서버는 connection.welcome을 먼저 보내며 payload는 server=AdofaiIpc, serverVersion,
protocolVersion, peerId, connectionId를 포함합니다. peer는 hello(protocolVersion3),
namespace별 subscribe를 보냅니다. subscribed 뒤 namespace.changed snapshot을 받습니다.
아직 없는 모드는 unavailable이며 등록·초기화에 따라 initializing, ready가 push됩니다.
ready 이후 모드의 최초 snapshot을 받습니다.

| kind | 의미 |
| --- | --- |
| command | 양쪽 어느 peer나 시작하는 이름 있는 작업. payload는 object. |
| accepted | router의 실행 수락. command.accepted와 원 명령 correlationId. |
| event | 이름 있는 결과·snapshot·자발적 변경. correlationId는 선택 사항. |
| rejected | command.rejected, payload code/message. |
| namespace | 수명 status/version/error snapshot. |
| ping/pong | 전송 계층의 연결 생존 확인. |

명령과 peer 이벤트를 보내기 전에 구독해야 합니다. 같은 연결의 최근1024개 command ID에서
중복을 거절합니다. 재연결은 명령을 재전송하지 않습니다. 앱은 새 snapshot과 도메인 ID로 작업을
조정합니다. generic result를 기다리는 RPC API는 제공하지 않습니다.

제어 메시지·frame2MiB, JSON depth32, peer64개, peer별 구독64개, 실행 수락 명령256개,
소켓별 대기 메시지256개/8MiB로 제한합니다. 느린 소비자는 연결을 끊고 재연결 후 snapshot을
복구합니다.10초마다 heartbeat를 보내고45초간 응답 없는 peer를 종료합니다. 큰 파일·오디오·픽셀은
JSON 대신 스트림이나 기존 파일/네이티브 전송을 사용하세요.

HTTP에는 /ipc/health와 /ipc/download/<ticket>만 유지합니다. POST /ipc와 namespace/method
discovery는 제거했습니다. job/lease/revision은 해당 앱이 payload에서 정의하고 검증합니다.
