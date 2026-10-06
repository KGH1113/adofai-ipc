# 모드 API

[목차](000-DevelopGuide.md) | [English](../en/002-ModApi.md)

모드마다 namespace 하나를 등록합니다. 새 handle은 initializing으로 시작합니다. 같은 namespace를
다시 등록하면 기존 handle을 취소하고 handler와 티켓을 제거한 뒤 새 handle을 만듭니다.
handler를 모두 다시 등록하세요. 같은 command 이름의 재등록은 해당 handler를 교체합니다.

```csharp
using AdofaiIpc;

var ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace("example-mod", new IpcNamespaceInfo {
    DisplayName = "Example Mod", Version = "1.0.0",
    AllowedOrigins = new[] { "https://example.com", "http://localhost", "http://127.0.0.1" }
});
ipc.RegisterMainThreadCommand("level.open", command => {
    string levelId = (string)command.Payload["levelId"];
    if (string.IsNullOrEmpty(levelId)) {
        command.Reject("invalid_level", "Choose a level first.");
        return;
    }
    OpenLevel(levelId); // application service; Unity work runs on the game thread
    command.Reply("level.opened", new { levelId });
});
ipc.PeerSubscribed += peer =>
    ipc.SendToPeer(peer.PeerId, "library.snapshot", new { revision = 1 });
ipc.MarkReady();

// On a domain change:
ipc.Publish("library.changed", new { revision = 2 });
// On disable/unload:
AdofaiIpc.AdofaiIpc.UnregisterNamespace("example-mod");
```

RegisterCommand는 background worker에서 실행하는 Action<IpcCommand>, RegisterAsyncCommand는
Func<IpcCommand,Task>를 받습니다. Unity를 접근하는 RegisterMainThreadCommand는 게임 스레드에
작업을 큐잉합니다. handler는 Reply, Reject 또는 Publish로 실제 도메인 결과를 표현해야 합니다.
Reject(code,message,details)는 필드 이름·행 번호 같은 도메인 검증 정보를 함께 전달합니다.
사용자 안내는 message에, 구조화한 데이터는 details에 두세요.
accepted는 실행을 허용했다는 뜻입니다. 완료를 의미하지 않습니다. 예외는 command.failed를
보내며, handler가 반환한 것만으로 자동 결과 이벤트를 만들지는 않습니다.

IpcCommand의 Id, Namespace, Name, PeerId, ConnectionId, Origin, JObject Payload,
CancellationToken을 사용합니다. peer identity는 서버가 정합니다. 연결 종료나 namespace 해제는
token을 취소합니다. 작업·lease·권한 token을 peer에 묶는 정책은 애플리케이션에서 구현합니다.

Publish는 허용된 구독자에게 이벤트를 보냅니다. SendToPeer는 구독한 특정 peer에 이벤트를 보내며
correlationId를 지정할 수 있습니다. SendCommandToPeer는 역방향 명령 ID를 반환합니다.
그 결과는 PeerEvent의 CorrelationId로 연결합니다. PeerSubscribed에서 초기 snapshot을 보내고
PeerDisconnected에서 소유 자원을 정리합니다. 구독 snapshot 콜백은 ready일 때 실행됩니다.

namespace는 소문자·숫자·하이픈, command/event는 여기에 점도 사용합니다. AllowedOrigins는 구독과
메시지를 제한합니다. 기본 포트 Origin을 허용하면 같은 scheme/host의 개발 포트도 허용하고,
명시한 비기본 포트는 정확히 일치해야 합니다. Origin 없는 네이티브 peer는 허용하며 빈 목록은
메시지 Origin을 제한하지 않습니다.

같은 프로세스의 모드는 CreateLocalPeer(name)을 자체 port로 감싸 주입할 수 있습니다.
Message listener를 먼저 등록한 뒤 Subscribe, SendCommand, SendEvent를 사용하고 Dispose합니다.
동일한 router를 사용하므로 소켓이나 HTTP polling이 필요하지 않습니다.

Local peer의 IsClosed는 IPC 중지나 peer 종료 후 true입니다. 중지된 peer를 재사용하지 말고
연결 adapter에서 새 peer와 listener·구독을 만드세요. 종료된 peer에 메시지를 보내면
ObjectDisposedException이 발생합니다. Dispose는 구독을 unavailable로 알리고 소유 자원을
정리하므로 이전 세션의 이벤트가 새 세션에 영향을 주지 않도록 session identity를 확인하세요.
