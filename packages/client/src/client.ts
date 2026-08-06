import {
  IpcConnectionError,
  IpcHttpError,
  IpcResponseError,
  IpcTimeoutError
} from "./errors";
import type {
  AdofaiIpcClientOptions,
  IpcCallOptions,
  IpcErrorInfo,
  IpcHealthResponse,
  IpcNamespaceDetail,
  IpcNamespacesResponse,
  IpcRequestId,
  IpcRequestOptions,
  IpcResponse,
  TryConnectOptions,
  WaitForNamespaceOptions
} from "./types";

const DEFAULT_HOST = "127.0.0.1";
const DEFAULT_START_PORT = 32145;
const DEFAULT_END_PORT = 32155;
const DEFAULT_PROBE_TIMEOUT_MS = 500;
const DEFAULT_REQUEST_TIMEOUT_MS = 10_000;
const DEFAULT_NAMESPACE_WAIT_TIMEOUT_MS = 10_000;
const DEFAULT_NAMESPACE_POLL_INTERVAL_MS = 100;

export class AdofaiIpcClient {
  readonly baseUrl: string;

  private readonly fetchImpl: typeof fetch;
  private readonly requestTimeoutMs: number;

  constructor(options: AdofaiIpcClientOptions = {}) {
    this.baseUrl = normalizeBaseUrl(options.baseUrl ?? `http://${DEFAULT_HOST}:${DEFAULT_START_PORT}`);
    this.fetchImpl = options.fetch ?? globalThis.fetch;
    this.requestTimeoutMs =
      options.requestTimeoutMs ?? options.timeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;

    if (!this.fetchImpl) {
      throw new IpcConnectionError("A fetch implementation is required.");
    }
  }

  static async connect(options: TryConnectOptions = {}): Promise<AdofaiIpcClient> {
    return tryConnect(options);
  }

  async health(options: IpcRequestOptions = {}): Promise<IpcHealthResponse> {
    return this.get<IpcHealthResponse>("/ipc/health", options);
  }

  async listNamespaces(options: IpcRequestOptions = {}): Promise<IpcNamespacesResponse> {
    return this.get<IpcNamespacesResponse>("/ipc/namespaces", options);
  }

  async getNamespace(
    namespace: string,
    options: IpcRequestOptions = {}
  ): Promise<IpcNamespaceDetail> {
    return this.get<IpcNamespaceDetail>(
      `/ipc/namespaces/${encodeURIComponent(namespace)}`,
      options
    );
  }

  async waitForNamespace(
    namespace: string,
    options: WaitForNamespaceOptions = {}
  ): Promise<IpcNamespaceDetail> {
    const timeoutMs = options.timeoutMs ?? DEFAULT_NAMESPACE_WAIT_TIMEOUT_MS;
    const pollIntervalMs = options.pollIntervalMs ?? DEFAULT_NAMESPACE_POLL_INTERVAL_MS;
    const requiredStatus = options.status ?? "registered";
    const deadline = Date.now() + timeoutMs;
    let stateError: IpcResponseError | undefined;

    while (true) {
      try {
        const detail = await this.getNamespace(namespace, {
          timeoutMs: options.requestTimeoutMs
        });

        if (requiredStatus === "registered" || detail.status === "ready") {
          return detail;
        }

        if (detail.status === "error") {
          throw new IpcResponseError({
            code: "namespace_error",
            message: detail.error?.message ?? `Namespace initialization failed: ${namespace}`
          });
        }

        if (detail.status !== "initializing") {
          throw new IpcResponseError({
            code: "namespace_status_unavailable",
            message: `Namespace status is unavailable: ${namespace}`
          });
        }

        stateError = new IpcResponseError({
          code: "namespace_initializing",
          message: `Namespace is initializing: ${namespace}`
        });
      } catch (error) {
        if (
          !(error instanceof IpcResponseError) ||
          (error.code !== "namespace_not_found" && error.code !== "namespace_initializing")
        ) {
          throw error;
        }

        stateError = error;
      }

      const remainingMs = deadline - Date.now();
      if (remainingMs <= 0) {
        throw stateError ?? new IpcResponseError({
          code: "namespace_initializing",
          message: `Namespace is initializing: ${namespace}`
        });
      }

      await delay(Math.min(pollIntervalMs, remainingMs));
    }
  }

  namespace(namespace: string): AdofaiIpcNamespaceClient {
    return new AdofaiIpcNamespaceClient(this, namespace);
  }

  async call<TResult = unknown, TParams = unknown>(
    options: IpcCallOptions<TParams>
  ): Promise<TResult> {
    const response = await this.post<IpcResponse<TResult>>(
      "/ipc",
      {
        namespace: options.namespace,
        method: options.method,
        params: options.params ?? {},
        id: options.id ?? createRequestId()
      },
      { timeoutMs: options.timeoutMs }
    );

    if (!response.ok) {
      throw new IpcResponseError(response.error);
    }

    return response.result;
  }

  private async get<TResult>(
    path: string,
    options: IpcRequestOptions = {}
  ): Promise<TResult> {
    return this.request<TResult>(path, {
      method: "GET"
    }, options);
  }

  private async post<TResult>(
    path: string,
    body: unknown,
    options: IpcRequestOptions = {}
  ): Promise<TResult> {
    return this.request<TResult>(path, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify(body)
    }, options);
  }

  private async request<TResult>(
    path: string,
    init: RequestInit,
    options: IpcRequestOptions
  ): Promise<TResult> {
    const timeoutMs = options.timeoutMs ?? this.requestTimeoutMs;
    const controller = new AbortController();
    const timeoutError = new IpcTimeoutError(timeoutMs);
    const timeout = setTimeout(() => controller.abort(timeoutError), timeoutMs);

    try {
      const response = await this.fetchImpl(`${this.baseUrl}${path}`, {
        ...init,
        signal: controller.signal
      });

      if (!response.ok) {
        const text = await response.text();
        const responseError = parseIpcResponseError(text);
        if (responseError) throw new IpcResponseError(responseError);
        throw new IpcHttpError(response.status, text || response.statusText);
      }

      return (await response.json()) as TResult;
    } catch (error) {
      if (error instanceof IpcHttpError || error instanceof IpcResponseError) throw error;
      if (controller.signal.aborted) {
        if (error === timeoutError) throw timeoutError;
        throw new IpcTimeoutError(timeoutMs, { cause: error });
      }
      throw new IpcConnectionError(getErrorMessage(error), { cause: error });
    } finally {
      clearTimeout(timeout);
    }
  }
}

export class AdofaiIpcNamespaceClient {
  constructor(
    private readonly client: AdofaiIpcClient,
    readonly namespace: string
  ) {
  }

  async call<TResult = unknown, TParams = unknown>(
    method: string,
    params?: TParams,
    idOrOptions?: IpcRequestId | IpcRequestOptions,
    options: IpcRequestOptions = {}
  ): Promise<TResult> {
    const requestOptions = isIpcRequestOptions(idOrOptions) ? idOrOptions : options;
    const id = isIpcRequestOptions(idOrOptions) ? undefined : idOrOptions;

    return this.client.call<TResult, TParams>({
      namespace: this.namespace,
      method,
      params,
      id,
      timeoutMs: requestOptions.timeoutMs
    });
  }
}

export async function tryConnect(options: TryConnectOptions = {}): Promise<AdofaiIpcClient> {
  const host = options.host ?? DEFAULT_HOST;
  const startPort = options.startPort ?? DEFAULT_START_PORT;
  const endPort = options.endPort ?? DEFAULT_END_PORT;
  const probeTimeoutMs =
    options.probeTimeoutMs ?? options.timeoutMs ?? DEFAULT_PROBE_TIMEOUT_MS;
  const requestTimeoutMs =
    options.requestTimeoutMs ?? options.timeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS;

  for (let port = startPort; port <= endPort; port++) {
    const client = new AdofaiIpcClient({
      baseUrl: `http://${host}:${port}`,
      fetch: options.fetch,
      requestTimeoutMs: probeTimeoutMs
    });

    try {
      const health = await client.health();

      if (health.ok && health.server === "AdofaiIpc") {
        return new AdofaiIpcClient({
          baseUrl: client.baseUrl,
          fetch: options.fetch,
          requestTimeoutMs
        });
      }
    } catch {
    }
  }

  throw new IpcConnectionError(
    `Could not connect to AdofaiIpc on ${host}:${startPort}-${endPort}.`
  );
}

function normalizeBaseUrl(value: string): string {
  return value.replace(/\/+$/, "");
}

function getErrorMessage(error: unknown): string | undefined {
  if (
    typeof error === "object" &&
    error !== null &&
    "message" in error &&
    typeof error.message === "string"
  ) {
    return error.message;
  }

  return undefined;
}

function parseIpcResponseError(text: string): IpcErrorInfo | undefined {
  try {
    const value = JSON.parse(text) as { error?: unknown };
    const error = value?.error;

    if (
      typeof error === "object" &&
      error !== null &&
      "code" in error &&
      typeof error.code === "string" &&
      "message" in error &&
      typeof error.message === "string"
    ) {
      return { code: error.code, message: error.message };
    }
  } catch {
  }

  return undefined;
}

function isIpcRequestOptions(value: unknown): value is IpcRequestOptions {
  return typeof value === "object" && value !== null;
}

function delay(timeoutMs: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, timeoutMs));
}

function createRequestId(): string {
  return `adofai-ipc-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
