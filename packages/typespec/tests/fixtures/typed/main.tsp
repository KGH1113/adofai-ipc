import "@adofai-ipc/typespec";

@ipc.mod("typed-test")
namespace TypedTest {
  model Entry {
    text: string;
    enabled: boolean;
    revision: int32;
  }

  enum Phase { Pending: "pending", Done: "done" }

  model RichEntry {
    labels: string[];
    maybeLabel: string | null;
    omitted?: string;
    optionalNullable?: string | null;
    ratio: float64;
    count: safeint;
    phase: Phase;
    literal: "fixed";
  }

  model FileBundle {
    content: ipc.File;
    files: ipc.File[];
    optional?: ipc.File | null;
  }

  op echo(entry: Entry): Entry;
  op add(left: int32, right: int32): int32;
  op ask(message: string): boolean;
  op appId(): string;
  op appRevision(): int32;
  op badResult(): string;
  op delay(milliseconds: int32): boolean;
  op increment(): int32;
  op executions(): int32;
  op echoRich(entry: RichEntry): RichEntry;
  op noOp(): void;
  op downloadText(): ipc.File;
  op downloadBundle(): FileBundle;
  op pushFile(): safeint;

  @ipc.app
  interface Browser {
    confirm(message: string): boolean;
    revision(): int32;
    updated(entry: RichEntry): void;
    receiveFile(file: ipc.File): safeint;
  }
}
