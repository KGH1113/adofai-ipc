# AdofaiIpc 개발 가이드 - 모드 API

## [목차로 이동](000-DevelopGuide.md)

1. [Namespace 등록](#1-namespace-%EB%93%B1%EB%A1%9D)
2. [Method 등록](#2-method-%EB%93%B1%EB%A1%9D)
3. [등록 해제](#3-%EB%93%B1%EB%A1%9D-%ED%95%B4%EC%A0%9C)
4. [Origin 제한](#4-origin-%EC%A0%9C%ED%95%9C)
5. [이름 규칙](#5-%EC%9D%B4%EB%A6%84-%EA%B7%9C%EC%B9%99)
6. [Lifecycle 예시](#6-lifecycle-%EC%98%88%EC%8B%9C)

---

## 1. Namespace 등록

AdofaiIpc를 사용하는 모드는 먼저 자신의 namespace를 등록해야 합니다.

```csharp
using AdofaiIpc;

AdofaiIpcNamespace ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace(
    "tufhelper2",
    new IpcNamespaceInfo {
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

namespace는 모드 단위로 하나를 소유하는 것을 권장합니다. 같은 namespace를 다시 등록하면
기존 namespace의 metadata가 갱신되고, 기존 method들은 유지됩니다.

---

## 2. Method 등록

일반 handler는 `Register`로 등록합니다.

```csharp
ipc.Register("health", request => new {
    ok = true,
    mod = "TUFHelper2"
});
```

Unity object, scene, level state, ADOFAI controller처럼 main thread가 필요한 작업은
`RegisterMainThread`를 사용합니다.

```csharp
ipc.RegisterMainThread("level.open", request => {
    // Unity 또는 ADOFAI 상태를 만지는 작업
    return new {
        opened = true
    };
});
```

같은 method를 다시 등록하면 기존 handler가 새 handler로 교체됩니다. 이 동작 덕분에
모드가 활성화될 때마다 register해도 중복 등록 예외가 발생하지 않습니다.

---

## 3. 등록 해제

method 하나만 해제하려면 namespace 객체에서 `Unregister`를 호출합니다.

```csharp
ipc.Unregister("health");
```

모드가 소유한 namespace 전체를 해제하려면 public facade에서
`UnregisterNamespace`를 호출합니다.

```csharp
AdofaiIpc.AdofaiIpc.UnregisterNamespace("tufhelper2");
```

Feature가 꺼질 때 namespace 전체를 내려야 하는 경우에는 namespace 이름을 상수로
관리하는 편이 안전합니다.

---

## 4. Origin 제한

브라우저에서 호출되는 IPC는 namespace별로 허용할 Origin을 지정할 수 있습니다.

```csharp
new IpcNamespaceInfo {
    DisplayName = "TUFHelper2",
    Version = Main.Instance.Version.ToString(),
    AllowedOrigins = new[] {
        "https://tuforums.com",
        "http://localhost",
        "http://127.0.0.1"
    }
}
```

`AllowedOrigins`가 `null`이거나 비어 있으면 해당 namespace는 Origin 제한을 두지 않습니다.
`Origin` header가 없는 curl, native app, local tool 요청도 허용됩니다.

`http://localhost`처럼 포트 없이 등록하면 `http://localhost:5173` 같은 개발 서버도
같은 scheme/host로 보고 허용합니다. 포트까지 제한하고 싶다면
`http://localhost:5173`처럼 정확한 Origin을 등록하면 됩니다.

> ⚠️ **주의**
>
> CORS는 브라우저가 따르는 정책일 뿐입니다. 실제 접근 제어는 AdofaiIpc가
> `POST /ipc` 요청의 `namespace`와 `Origin`을 함께 검사해서 수행합니다.

---

## 5. 이름 규칙

namespace와 method는 HTTP client가 직접 전달하는 public protocol 값입니다.
읽기 쉽고 충돌이 적은 이름을 사용해주세요.

* namespace
    * 소문자, 숫자, `-` 사용 가능
    * 예: `tufhelper2`, `tuf-replay`
* method
    * 소문자, 숫자, `-`, `.` 사용 가능
    * 예: `health`, `level.open`, `records.list`

namespace에는 `.`을 사용할 수 없습니다.

---

## 6. Lifecycle 예시

활성화 시 register하고 비활성화 시 unregister하는 흐름을 권장합니다.

```csharp
using AdofaiIpc;

public sealed class IpcFeature {
    private const string Namespace = "tufhelper2";

    public void Enable() {
        AdofaiIpcNamespace ipc = AdofaiIpc.AdofaiIpc.RegisterNamespace(
            Namespace,
            new IpcNamespaceInfo {
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
