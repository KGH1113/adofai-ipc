import { createTypeSpecLibrary, paramMessage } from "@typespec/compiler";

export const $lib = createTypeSpecLibrary({
  name: "@adofai-ipc/typespec",
  diagnostics: {
    contract: { severity: "error", messages: { default: paramMessage`${"message"}` } },
  },
});

export const modKey = Symbol.for("adofai-ipc.mod");
export const appKey = Symbol.for("adofai-ipc.app");
export const namespace = "ipc";

export function $mod(context, target, id) {
  if (!/^[a-z0-9-]+$/.test(id)) {
    $lib.reportDiagnostic(context.program, { code: "contract", target, format: { message: "A mod ID must contain only lowercase letters, numbers and hyphens." } });
    return;
  }
  context.program.stateMap(modKey).set(target, id);
}

export function $app(context, target) {
  context.program.stateSet(appKey).add(target);
}

export { $onEmit } from "./generate.js";
