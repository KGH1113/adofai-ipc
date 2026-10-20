import "../dist/index.js";

namespace ipc;

/** A file sent by the mod. The SDK handles the underlying transfer. */
model File {}

extern dec `mod`(target: TypeSpec.Reflection.Namespace, id: valueof string);
extern dec app(target: TypeSpec.Reflection.Interface);
