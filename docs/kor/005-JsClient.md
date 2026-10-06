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
위해 시작을 분리합니다. 기본 host127.0.0.1, port32145..32155, 포트별 timeout1500ms입니다.
transportFactory와 onProtocolMismatch를 주입할 수 있습니다. browser는 global WebSocket을
사용하고 native나 WebSocket 없는 Node 환경은 adapter를 주입합니다.

IpcTransport port는 connect, send, close, onMessage, onClose를 제공합니다. send/close는
비동기여도 됩니다. 메시지는 UTF-8 JSON 문자열이며 전달받은 subprotocol을 지원해야 합니다.
실패한 probe의 소켓과 listener는 정리합니다.

connection: namespace, onState, state, peerId, connectionId, close.
state는 disconnected/connected/reconnecting/closed/incompatible이며 onState는 현재 값을
즉시 알립니다. 성립된 연결은 jitter/backoff로 복구하고 재구독하지만 명령을 재전송하지 않습니다.

channel: on, onCommand, send, emit, reply, onStatus, status, error, whenReady.
send(name,payload,{id,signal})는 ID를 즉시 반환합니다. AbortSignal은 전송 전에만 검사합니다.
이미 실행 중인 작업은 도메인 취소 명령으로 취소하세요. on/onCommand/onStatus의 반환 함수로
listener를 해제합니다. 상관관계, 도메인 timeout, 연결 종료 시 작업 실패, projection은 앱이 맡습니다.

최초 부재는 IpcUnavailableError(UNAVAILABLE), wire 불일치는 IpcProtocolMismatchError(INCOMPATIBLE).
whenReady는 polling 없이 상태 이벤트를 기다리며 기본15000ms 후 IpcTimeoutError(TIMEOUT),
초기화 실패 시 모드 code를 가진 IpcNamespaceError를 반환합니다. isIpcUnavailable은 UNAVAILABLE만
식별합니다. 최초 서버 발견 전의 재시도는 앱 supervisor, 성공 후 연결 복구는 SDK가 담당합니다.
