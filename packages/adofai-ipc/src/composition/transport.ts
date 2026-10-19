import type { ConnectIpcOptions } from "../api/options";
import { MessageConnection } from "../adapters/messaging/message-connection";
import { browserTransportFactory } from "../adapters/websocket/browser-transport";

export class IpcConnection extends MessageConnection {
  constructor(options: ConnectIpcOptions = {}) {
    super(options, options.transportFactory ?? browserTransportFactory);
  }
}
export async function connectIpc(options: ConnectIpcOptions = {}): Promise<IpcConnection> {
  const connection = new IpcConnection(options);
  await connection.start();
  return connection;
}
