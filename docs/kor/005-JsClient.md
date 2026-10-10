# TypeScript SDK

[목차](000-DevelopGuide.md) | [English](../en/005-JsClient.md)

v1 준비 중에는 npm 패키지 없이 원본 소스를 소비합니다.

```bash
./scripts/run.sh client-sync /path/to/consumer/vendor/adofai-ipc
```

생성한 src, LICENSE, README.md, source.json을 소비 프로젝트에 커밋합니다. source.json은 upstream
commit, 수정 여부, SDK/wire version, 파일별 SHA-256을 기록합니다. 원본 수정 후 다시 생성하세요.
절대 경로 의존성과 수동 transport 복사를 피하고 Vite/TS alias를 raw source에 연결합니다.
registry dependency를 제거하고 lockfile도 갱신합니다. tsup 전용 버전 치환은 필요 없습니다.

```ts
import { IpcConnection } from "./vendor/adofai-ipc/src";

// Register listeners before connecting so the initial snapshot cannot be lost.
const connection = new IpcConnection();
const channel = connection.namespace("example-mod");
channel.on<{ revision: number }>("library.snapshot", snapshot => projectLibrary(snapshot));
channel.on<{ revision: number }>("library.changed", change => projectLibrary(change));
channel.on("level.opened", (payload, message) => applyOpenedLevel(payload, message.correlationId));
channel.on("command.rejected", (error, message) => showFailure(error, message.correlationId));
await connection.start();
await channel.whenReady({ timeoutMs: 15_000 });
const commandId = channel.send("level.open", { levelId: "abc" });

channel.onCommand("render.frame", (frame, message) => {
    // Perform the domain operation; report its actual completion.
    channel.reply(message.id, "render.frame.ready", { path: "/tmp/frame.png" });
});
// On application shutdown:
connection.close();
```

connectIpc(options)는 연결을 시작합니다. new IpcConnection(options)는 listener를 먼저 설치하기
위해 시작을 분리합니다. 기본 host127.0.0.1, port32145..32155, 포트별 timeout8000ms입니다.
transportFactory와 onProtocolMismatch를 주입할 수 있습니다. browser는 global WebSocket을
사용하고 native나 WebSocket 없는 Node 환경은 adapter를 주입합니다.

IpcTransport port는 connect, send, close, onMessage, onClose를 제공합니다. send/close는
비동기여도 됩니다. 메시지는 UTF-8 JSON 문자열이며 전달받은 subprotocol을 지원해야 합니다.
실패한 probe의 소켓과 listener는 정리합니다.

connection: namespace, onState, state, peerId, connectionId, close.
state는 disconnected/connected/reconnecting/closed/incompatible이며 onState는 현재 값을
즉시 알립니다. 최초 서버 부재도 reconnecting으로 들어가 jitter/backoff로 재탐색합니다.
start()는 첫 탐색이 끝나면 반환하므로 실제 모드 준비는 channel.whenReady()로 기다립니다.
동시에 호출한 start는 하나의 탐색을 공유하며, 복구 후 재구독하지만 명령을 재전송하지 않습니다.
checkConnection()은 유지된 소켓의 응답을 확인하거나 대기 중인 재탐색을 깨웁니다.
heartbeatIntervalMs(기본10000ms), heartbeatTimeoutMs(기본15000ms)로 ping/pong 응답을 확인해
close 이벤트 없이 끊어진 소켓도 교체합니다. browser transport는 pagehide 때 소켓을 해제하고
연결 후 error도 연결 종료로 처리합니다. close()는 진행 중인 탐색과 heartbeat timer를 취소합니다.
재탐색은 마지막 성공 포트부터 시작합니다. Chromium은 같은 renderer에서 실패한 연결이 쌓이면
WebSocket 시작을 최대5초 지연시킬 수 있어 기본8초 동안 handshake를 기다립니다. 너무 짧은
timeout은 게임을 켠 뒤에도 지연된 시도를 계속 취소하며, 페이지 새로고침으로도 이어질 수 있습니다.

channel: on, onCommand, send, emit, reply, onStatus, status, error, whenReady.
send(name,payload,{id,signal})는 ID를 즉시 반환합니다. AbortSignal은 전송 전에만 검사합니다.
이미 실행 중인 작업은 도메인 취소 명령으로 취소하세요. on/onCommand/onStatus의 반환 함수로
listener를 해제합니다. 상관관계, 도메인 timeout, 연결 종료 시 작업 실패, projection은 앱이 맡습니다.

wire 불일치는 IpcProtocolMismatchError(INCOMPATIBLE)를 반환하고 재탐색을 중단합니다.
whenReady는 polling 없이 상태 이벤트를 기다리며 기본15000ms 후 IpcTimeoutError(TIMEOUT),
초기화 실패 시 모드 code를 가진 IpcNamespaceError를 반환합니다. 준비 대기 timeout 뒤에도 SDK는
재탐색을 계속하므로 앱은 channel.onStatus의 ready 전환 때 읽기 상태를 복구해야 합니다.
연결 없이 전송하거나 명시적으로 닫힌 연결을 시작하면 IpcUnavailableError(UNAVAILABLE)이며,
isIpcUnavailable은 UNAVAILABLE만 식별합니다. 변경 명령은 재접속해도 자동 재전송하지 않습니다.
