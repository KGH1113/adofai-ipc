# ADOFAI IPC TypeSpec generator

Define a shared IPC contract and generate matching TypeScript and C# APIs for ADOFAI mods and apps.

```bash
pnpm add -D @adofai-ipc/typespec
pnpm add adofai-ipc
```

Create a TypeSpec contract:

```typespec
import "@adofai-ipc/typespec";

@ipc.mod("echo")
namespace Echo {
  op echo(text: string): string;
}
```

Configure the contract and output paths in `adofai-ipc.config.json`, then generate or verify the outputs:

```bash
pnpm exec adofai-ipc generate
pnpm exec adofai-ipc generate --check
```

See the [documentation](https://adofai-ipc.impl1113.dev/docs/) for configuration, supported types, and mod/app integration.
