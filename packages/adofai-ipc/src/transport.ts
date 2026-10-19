export { connectIpc, IpcConnection } from "./composition/transport";
export { IpcChannel } from "./adapters/messaging/message-channel";
export { browserTransportFactory } from "./adapters/websocket/browser-transport";
export * from "./api/connection-errors";
export type * from "./api/options";
export type * from "./models/messaging";
export type * from "./ports/transport";
export { CLIENT_VERSION, PROTOCOL_VERSION, IPC_SUBPROTOCOL } from "./models/protocol";
