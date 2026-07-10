# AdofaiIpc Developer Guide - HTTP Protocol

[Guide index](000-DevelopGuide.md) | [Korean](../003-HttpProtocol.md)

1. [Call endpoint](#1-call-endpoint)
2. [Request body](#2-request-body)
3. [Response body](#3-response-body)
4. [Origin policy](#4-origin-policy)
5. [Discovery endpoints](#5-discovery-endpoints)

---

## 1. Call endpoint

Clients call mod methods through one endpoint:

```http
POST /ipc
Content-Type: application/json
```

The server routes the request using the `namespace` and `method` fields.

---

## 2. Request body

```json
{
  "namespace": "example-mod",
  "method": "level.open",
  "params": {
    "id": "1234"
  },
  "id": "open-1234"
}
```

- `namespace`: namespace registered by the target mod
- `method`: method registered inside that namespace
- `params`: method-specific JSON data; use an empty object when no parameters are needed
- `id`: optional request identifier returned in the response

---

## 3. Response body

A successful call returns the handler result:

```json
{
  "ok": true,
  "result": {
    "accepted": true
  },
  "id": "open-1234"
}
```

A failed call returns a stable error code and a human-readable message:

```json
{
  "ok": false,
  "error": {
    "code": "handler_not_found",
    "message": "No handler registered for example-mod:level.open"
  },
  "id": "open-1234"
}
```

Common error codes include `invalid_request`, `invalid_namespace`, `invalid_method`,
`namespace_not_found`, `handler_not_found`, `handler_failed`, `origin_not_allowed`, and
`internal_error`.

---

## 4. Origin policy

Browser requests may include an `Origin` header. AdofaiIpc checks the target namespace's
`AllowedOrigins` when it handles the actual `POST /ipc` request.

An origin that is not allowed receives HTTP `403` with the `origin_not_allowed` error code.

```json
{
  "ok": false,
  "error": {
    "code": "origin_not_allowed",
    "message": "Origin is not allowed for namespace: example-mod"
  },
  "id": "open-1234"
}
```

An `OPTIONS /ipc` preflight request does not include a namespace body. AdofaiIpc therefore
returns common CORS headers for preflight and enforces namespace-specific access on the
subsequent `POST /ipc` request.

Requests without an `Origin` header are allowed.

---

## 5. Discovery endpoints

Check the server and active port:

```http
GET /ipc/health
```

List registered namespaces:

```http
GET /ipc/namespaces
```

Inspect one namespace and its methods:

```http
GET /ipc/namespaces/example-mod
```
