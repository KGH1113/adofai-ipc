# AdofaiIpc 개발 가이드 - 라이프사이클

## [목차로 이동](000-DevelopGuide.md)

1. [Listener 소유권](#1-listener-%EC%86%8C%EC%9C%A0%EA%B6%8C)
2. [Register와 Unregister](#2-register%EC%99%80-unregister)
3. [Thread 구분](#3-thread-%EA%B5%AC%EB%B6%84)
4. [Origin policy와 lifecycle](#4-origin-policy%EC%99%80-lifecycle)
5. [모드 lifecycle 패턴](#5-%EB%AA%A8%EB%93%9C-lifecycle-%ED%8C%A8%ED%84%B4)

---

## 1. Listener 소유권

AdofaiIpc는 하나의 HTTP listener를 소유하는 infrastructure mod입니다.

다른 모드는 별도의 localhost 서버를 열지 않고, AdofaiIpc registry에 namespace와
method만 등록합니다. 이렇게 하면 여러 모드가 동시에 브라우저나 외부 도구와 통신해도
포트 충돌을 줄이고 client protocol을 하나로 유지할 수 있습니다.

---

## 2. Register와 Unregister

AdofaiIpc registry는 같은 namespace 또는 method가 다시 등록되어도 예외를 던지지 않고
최신 정보로 갱신합니다.

따라서 모드가 다시 활성화되어도 같은 register 코드를 반복해서
실행할 수 있습니다.

기능이 꺼질 때는 해당 모드가 소유한 namespace를 해제하는 것을 권장합니다.

```csharp
public void Disable() {
    AdofaiIpc.AdofaiIpc.UnregisterNamespace("tufhelper2");
}
```

namespace 전체가 아니라 일부 method만 내리고 싶다면 `AdofaiIpcNamespace.Unregister`를
사용할 수 있습니다.

---

## 3. Thread 구분

HTTP 요청은 AdofaiIpc listener thread에서 들어옵니다.

파일 읽기, JSON 변환, 단순 계산처럼 Unity 상태를 건드리지 않는 작업은 일반 `Register`로
처리할 수 있습니다.

반대로 다음과 같은 작업은 Unity main thread에서 실행되어야 합니다.

* Unity object 생성 또는 접근
* scene, level, editor 상태 변경
* ADOFAI controller 또는 game state 접근
* Unity API 호출

이 경우에는 `RegisterMainThread`를 사용합니다.

```csharp
ipc.RegisterMainThread("level.open", request => {
    // Unity main thread에서 실행되어야 하는 작업
    return new {
        opened = true
    };
});
```

---

## 4. Origin policy와 lifecycle

Origin policy는 namespace metadata의 일부입니다. 따라서 namespace를 등록하는 시점에
`AllowedOrigins`도 함께 전달하는 것을 권장합니다.

같은 namespace를 다시 등록하면 metadata가 갱신되므로, `OnEnable()`에서 register를
반복해도 최신 Origin policy가 적용됩니다.

```csharp
AdofaiIpc.AdofaiIpc.RegisterNamespace(
    "tufhelper2",
    new AdofaiIpc.IpcNamespaceInfo {
        DisplayName = "TUFHelper2",
        Version = Main.Instance.Version.ToString(),
        AllowedOrigins = new[] {
            "https://tuforums.com",
            "http://localhost",
            "http://127.0.0.1"
        }
    }
);
```

---

## 5. 모드 lifecycle 패턴

AdofaiIpc를 사용하는 모드는 IPC 등록을 하나의 Feature로 분리하는 방식을 권장합니다.

```csharp
public sealed class IpcFeature {
    private const string Namespace = "tufhelper2";

    public void Enable() {
        var ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace(
            Namespace,
            new AdofaiIpc.IpcNamespaceInfo {
                DisplayName = "TUFHelper2",
                Version = Main.Instance.Version.ToString(),
                AllowedOrigins = new[] {
                    "https://tuforums.com",
                    "http://localhost",
                    "http://127.0.0.1"
                }
            }
        );

        ipc.Register("health", Health);
        ipc.RegisterMainThread("level.open", OpenLevel);
    }

    public void Disable() {
        AdofaiIpc.AdofaiIpc.UnregisterNamespace(Namespace);
    }

    private static object Health(AdofaiIpc.Core.IpcRequest request) {
        return new {
            ok = true
        };
    }

    private static object OpenLevel(AdofaiIpc.Core.IpcRequest request) {
        return new {
            accepted = true
        };
    }
}
```

이 패턴은 모드의 활성화/비활성화 흐름과 IPC registry 상태를 함께 맞춰줍니다.
