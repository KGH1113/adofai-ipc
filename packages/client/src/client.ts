import { IpcConnectionError, IpcHttpError, IpcResponseError } from "./errors";
import type {
  AdofaiIpcClientOptions,
  IpcCallOptions,
  IpcHealthResponse,
  IpcNamespaceDetail,
  IpcNamespacesResponse,
  IpcResponse,
  TryConnectOptions
} from "./types";

const DEFAULT_HOST = "127.0.0.1";
const DEFAULT_START_PORT = 32145;
const DEFAULT_END_PORT = 32155;
const DEFAULT_TIMEOUT_MS = 500;

export class AdofaiIpcClient {
  readonly baseUrl: string;

  private readonly fetchImpl: typeof fetch;
  private readonly timeoutMs: number;

  constructor(options: AdofaiIpcClientOptions = {}) {
    this.baseUrl = normalizeBaseUrl(options.baseUrl ?? `http://${DEFAULT_HOST}:${DEFAULT_START_PORT}`);
    this.fetchImpl = options.fetch ?? globalThis.fetch;
    this.timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;

    if (!this.fetchImpl) {
      throw new IpcConnectionError("A fetch implementation is required.");
    }
  }

  static async connect(options: TryConnectOptions = {}): Promise<AdofaiIpcClient> {
    return tryConnect(options);
  }

  async health(): Promise<IpcHealthResponse> {
    return this.get<IpcHealthResponse>("/ipc/health");
  }

  async listNamespaces(): Promise<IpcNamespacesResponse> {
    return this.get<IpcNamespacesResponse>("/ipc/namespaces");
  }

  async getNamespace(namespace: string): Promise<IpcNamespaceDetail> {
    return this.get<IpcNamespaceDetail>(`/ipc/namespaces/${encodeURIComponent(namespace)}`);
  }

  namespace(namespace: string): AdofaiIpcNamespaceClient {
    return new AdofaiIpcNamespaceClient(this, namespace);
  }

  async call<TResult = unknown, TParams = unknown>(
    options: IpcCallOptions<TParams>
  ): Promise<TResult> {
    const response = await this.post<IpcResponse<TResult>>("/ipc", {
      namespace: options.namespace,
      method: options.method,
      params: options.params ?? {},
      id: options.id ?? createRequestId()
    });

    if (!response.ok) {
      throw new IpcResponseError(response.error);
    }

    return response.result;
  }

  private async get<TResult>(path: string): Promise<TResult> {
    return this.request<TResult>(path, {
      method: "GET"
    });
  }

  private async post<TResult>(path: string, body: unknown): Promise<TResult> {
    return this.request<TResult>(path, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify(body)
    });
  }

  private async request<TResult>(path: string, init: RequestInit): Promise<TResult> {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const response = await this.fetchImpl(`${this.baseUrl}${path}`, {
        ...init,
        signal: controller.signal
      });

      if (!response.ok) {
        const text = await response.text();
        throw new IpcHttpError(response.status, text || response.statusText);
      }

      return (await response.json()) as TResult;
    } catch (error) {
      if (error instanceof IpcHttpError) throw error;
      if (error instanceof Error) throw new IpcConnectionError(error.message);
      throw new IpcConnectionError();
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
    id?: string
  ): Promise<TResult> {
    return this.client.call<TResult, TParams>({
      namespace: this.namespace,
      method,
      params,
      id
    });
  }
}

export async function tryConnect(options: TryConnectOptions = {}): Promise<AdofaiIpcClient> {
  const host = options.host ?? DEFAULT_HOST;
  const startPort = options.startPort ?? DEFAULT_START_PORT;
  const endPort = options.endPort ?? DEFAULT_END_PORT;

  for (let port = startPort; port <= endPort; port++) {
    const client = new AdofaiIpcClient({
      baseUrl: `http://${host}:${port}`,
      fetch: options.fetch,
      timeoutMs: options.timeoutMs
    });

    try {
      const health = await client.health();

      if (health.ok && health.server === "AdofaiIpc") {
        return client;
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

function createRequestId(): string {
  return `adofai-ipc-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
