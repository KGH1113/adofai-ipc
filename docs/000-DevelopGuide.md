# AdofaiIpc 개발 가이드

## 소개

AdofaiIpc는 A Dance of Fire and Ice 모드들이 하나의 localhost HTTP listener를
공유할 수 있도록 만든 독립형 UnityModManager IPC 모드입니다.

각 모드는 직접 HTTP 서버를 열지 않고, AdofaiIpc에 자신의 namespace와 method를
등록합니다. 외부 클라이언트는 JSON body에 `namespace`, `method`, `params`,
`id`를 담아 하나의 IPC endpoint로 요청합니다.

> ⚠️ **주의**
>
> AdofaiIpc는 다른 모드가 참조하는 public API assembly이기도 합니다.
> 모드 생명주기에서는 활성화/비활성화 흐름에 맞춰
> register/unregister를 명확히 해주세요.

---

## 목차

1. [시작하기](001-GettingStarted.md)
2. [모드 API](002-ModApi.md)
3. [HTTP 프로토콜](003-HttpProtocol.md)
4. [라이프사이클](004-Lifecycle.md)
5. [JavaScript/TypeScript Client](005-JsClient.md)
