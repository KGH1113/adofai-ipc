import type { AppOptions, IpcOptions } from "../api/options";
import { RegisteredApp } from "../application/registration/registered-app";
import { createAppId } from "../adapters/crypto/create-app-id";
import { IpcConnection } from "./transport";
import { readHttpFile } from "../adapters/http/read-file";
import { downloadBrowserFile } from "../adapters/browser/download-file";

/** Default browser composition; application services only see the MessageSession port. */
export class AppRegistration extends RegisteredApp {
  constructor(options: AppOptions, settings: IpcOptions = {}) {
    super(options, new IpcConnection(settings), createAppId(), createAppId, settings.requestTimeoutMs, readHttpFile, downloadBrowserFile);
  }
}
export function createIpc(options: IpcOptions = {}): { registerApp(options: AppOptions): AppRegistration } {
  return { registerApp: app => new AppRegistration(app, options) };
}
export const ipc: ReturnType<typeof createIpc> = createIpc();
