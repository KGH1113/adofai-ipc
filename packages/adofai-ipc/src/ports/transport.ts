/** Byte transport only. Protocol, subscription and reconnection belong to the SDK. */
export interface IpcTransport {
  connect(): Promise<void>;
  send(text: string): void | Promise<void>;
  close(): void | Promise<void>;
  onMessage(listener: (text: string) => void): () => void;
  onClose(listener: (reason?: unknown) => void): () => void;
}
export type IpcTransportFactory = (url: string, protocol: string) => IpcTransport;
