# AdofaiIpc 개발 가이드 - HTTP 프로토콜

## [목차로 이동](000-DevelopGuide.md)

1. [호출 endpoint](#1-%ED%98%B8%EC%B6%9C-endpoint)
2. [요청 body](#2-%EC%9A%94%EC%B2%AD-body)
3. [응답 body](#3-%EC%9D%91%EB%8B%B5-body)
4. [Origin policy](#4-origin-policy)
5. [Discovery endpoint](#5-discovery-endpoint)

---

## 1. 호출 endpoint

클라이언트가 모드 method를 호출할 때 사용하는 기본 endpoint는 다음과 같습니다.

```http
POST /ipc
Content-Type: application/json
```

AdofaiIpc는 localhost에서만 요청을 받는 것을 전제로 합니다.

---

## 2. 요청 body

요청 body는 `namespace`와 `method`를 분리해서 전달합니다.

```json
{
  "namespace": "tufhelper2",
  "method": "level.open",
  "params": {
    "id": "1234"
  },
  "id": "open-1234"
}
```

각 필드의 의미는 다음과 같습니다.

* **`namespace`**
    * 요청을 받을 모드 namespace입니다.
* **`method`**
    * namespace 안에 등록된 method 이름입니다.
* **`params`**
    * handler에 전달할 JSON payload입니다.
    * 값이 필요 없는 method는 `{}`를 사용할 수 있습니다.
* **`id`**
    * 클라이언트가 응답 매칭에 사용할 수 있는 값입니다.
    * 응답 body에도 그대로 포함됩니다.

---

## 3. 응답 body

성공 응답은 `ok: true`와 handler 결과를 포함합니다.

```json
{
  "ok": true,
  "result": {
    "accepted": true
  },
  "id": "open-1234"
}
```

실패 응답은 `ok: false`와 error 객체를 포함합니다.

```json
{
  "ok": false,
  "error": {
    "code": "handler_not_found",
    "message": "No handler registered for tufhelper2:level.open"
  },
  "id": "open-1234"
}
```

error code는 잘못된 요청, 없는 namespace, 없는 method, handler 실행 실패 등을
클라이언트가 구분할 수 있도록 제공됩니다.

---

## 4. Origin policy

브라우저 요청에는 `Origin` header가 포함될 수 있습니다. AdofaiIpc는 namespace별
`AllowedOrigins` 설정을 보고 실제 `POST /ipc` 요청을 허용하거나 거부합니다.

허용되지 않은 Origin에서 등록된 namespace를 호출하면 `403`과 함께
`origin_not_allowed` error code가 반환됩니다.

```json
{
  "ok": false,
  "error": {
    "code": "origin_not_allowed",
    "message": "Origin is not allowed for namespace: tufhelper2"
  },
  "id": "open-1234"
}
```

preflight인 `OPTIONS /ipc` 요청에는 JSON body가 없어서 어떤 namespace를 호출할지
알 수 없습니다. 따라서 CORS preflight는 공통 CORS header로 응답하고, namespace별
접근 제어는 실제 `POST /ipc` 단계에서 수행합니다.

에러 우선순위는 다음 흐름을 권장합니다.

* JSON이 깨진 경우: `invalid_request`
* namespace가 없거나 형식이 잘못된 경우: `invalid_namespace`
* namespace가 등록되지 않은 경우: `namespace_not_found`
* namespace가 등록되어 있지만 Origin이 허용되지 않은 경우: `origin_not_allowed`
* method가 등록되지 않은 경우: `handler_not_found`

`Origin` header가 없는 curl, native app, local tool 요청은 브라우저 CORS 요청이 아니므로
Origin policy 검사에서 허용됩니다.

---

## 5. Discovery endpoint

AdofaiIpc는 클라이언트가 현재 IPC 서버와 등록된 namespace를 확인할 수 있도록
discovery endpoint를 제공합니다.

```http
GET /ipc/health
```

서버 상태와 protocol 정보를 확인합니다.

```http
GET /ipc/namespaces
```

등록된 namespace 목록을 확인합니다.

```http
GET /ipc/namespaces/tufhelper2
```

특정 namespace의 metadata와 method 목록을 확인합니다.

클라이언트는 `32145`부터 fallback 포트 후보에 대해 `/ipc/health`를 호출해서
현재 실행 중인 AdofaiIpc listener를 찾을 수 있습니다.
