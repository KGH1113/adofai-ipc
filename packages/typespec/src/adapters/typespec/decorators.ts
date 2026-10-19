import type { DecoratorContext, Namespace, Interface } from "@typespec/compiler";
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

export function $mod(context: DecoratorContext, target: Namespace, id: string) {
  if (!/^[a-z0-9-]+$/.test(id)) {
    $lib.reportDiagnostic(context.program, { code: "contract", target, format: { message: "A mod ID must contain only lowercase letters, numbers and hyphens." } });
    return;
  }
  context.program.stateMap(modKey).set(target, id);
}

export function $app(context: DecoratorContext, target: Interface) {
  context.program.stateSet(appKey).add(target);
}
