# 모드 API

[목차](000-DevelopGuide.md) | [English](../en/002-ModApi.md)

소비 모드는 Contracts와 Loader만 참조합니다. 공용 계약에는 Unity·Newtonsoft.Json·모드 DTO 타입이 없습니다.
IPC 어댑터에서 도메인 payload를 JSON으로 직렬화합니다.

```csharp
using System.Threading.Tasks;
using AdofaiIpc.Contracts;
using AdofaiIpc.Loader;
using Newtonsoft.Json.Linq;

IIpcRuntime runtime = IpcLoader.GetRuntime(modsDirectory,
    new RuntimeRequirements("2.0.0", requiredCapabilities: new[] { "commands", "game-thread" }));

IFeatureRegistration registration = runtime.RegisterFeature(
    new FeatureDescription("example-mod", "1.0.0", protocolMajor: 1),
    feature => {
        feature.OnCommand("level.open", IpcExecutionContext.GameThread, command => {
            string id = (string)JObject.Parse(command.Json)["levelId"];
            if (string.IsNullOrEmpty(id)) {
                command.Reject("invalid_level", "레벨을 먼저 선택하세요.");
                return Task.CompletedTask;
            }
            OpenLevel(id);
            command.Reply("level.opened", new JObject { ["levelId"] = id }.ToString());
            return Task.CompletedTask;
        });
        feature.OnClientAttached(client =>
            client.Send("library.snapshot", "{\"revision\":1}"));
    });
registration.MarkReady();

// 도메인 변경 시:
registration.Publish("library.changed", "{\"revision\":2}");
// 비활성화/unload 시:
registration.Dispose();
```

등록은 initializing으로 시작합니다. 같은 기능 이름을 중복 등록하면 기존 등록을 교체하지 않고 실패합니다.
configure에서 예외가 발생하면 해당 등록만 해제합니다. builder는 configure 중에만 사용합니다.
Dispose는 여러 번 호출해도 안전하며 명령 token·티켓을 취소하고 unavailable을 알립니다.
다시 켤 때는 새로 등록합니다.

OnCommand는 비동기 handler를 받습니다. GameThread는 최초 호출 스레드만 선택하며 Background는 worker를 사용합니다.
Reply는 명령 ID를 correlationId로 가진 이름 있는 결과를 요청한 client에 보냅니다.
Reject의 세 번째 detailsJson 인자로 구조화한 오류를 보낼 수 있습니다.
accepted는 실행 허용이며 완료가 아닙니다. handler 예외는 command.failed로 전달합니다.

OnClientAttached·OnClientDetached·OnEvent는 게임 스레드에서 실행합니다.
Attached는 ready 뒤 호출되므로 최초 구독·재연결의 현재 snapshot을 보냅니다.
command.Client의 서버가 정한 식별자로 소유권을 관리합니다.
연결 종료·구독 해제·기능 Dispose·호스트 종료는 명령 token을 취소합니다.
실행 중인 handler는 취소 token을 직접 관찰해야 합니다.

Publish는 허용된 구독자에게, SendToPeer는 특정 구독자에게 이벤트를 보냅니다.
SendCommandToPeer는 역방향 명령 ID를 반환하며 OnEvent의 CorrelationId로 결과를 연결합니다.
CreateLocalPeer는 소켓 없이 같은 router를 사용합니다. Message를 설치한 뒤 Subscribe,
SendCommand/SendEvent, Unsubscribe, Dispose합니다. Local Message는 worker에서 호출됩니다.

기능 이름은 소문자·숫자·하이픈, 메시지 이름은 점도 허용합니다.
AllowedOrigins로 접근을 제한하며 Origin 없는 native peer는 허용합니다.
