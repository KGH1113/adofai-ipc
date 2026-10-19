import { ipc } from "../dist/index.js";
import { Phase, TypedTestContract, type RichEntry } from "../../typespec/test/fixtures/typed/generated/index.js";

const app = ipc.registerApp({ displayName: "Typed UI" });
const channel = app.use(TypedTestContract, {
  confirm: ({ message }) => message.length > 0,
  revision: () => 1,
  updated: () => {},
});
const sum: Promise<number> = channel.add({ left: 1, right: 2 });
const text: Promise<string> = channel.appId();
void sum; void text;
const rich: RichEntry = { labels: [], maybeLabel: null, ratio: 1.5, count: 9007199254740991, phase: Phase.Done, literal: "fixed" };
const roundTrip: Promise<RichEntry> = channel.echoRich({ entry: rich });
const noResult: Promise<void> = channel.noOp();
const session: string | undefined = channel.sessionId;
const off = channel.onStatus((status, error) => { const code: string | undefined = error?.code; void status; void code; });
void roundTrip; void noResult; void session; off();
// @ts-expect-error literal values are constrained
const wrongLiteral: RichEntry = { ...rich, literal: "other" };
// @ts-expect-error nullable is independent from optional
const wrongOptional: RichEntry = { ...rich, omitted: null };
// @ts-expect-error enum values come from a string union
const wrongEnum: RichEntry = { ...rich, phase: "unknown" };
void wrongLiteral; void wrongOptional; void wrongEnum;
// @ts-expect-error no such method
channel.ad({ left: 1, right: 2 });
// @ts-expect-error int32 maps to number
channel.add({ left: "1", right: 2 });
// @ts-expect-error unknown argument
channel.add({ left: 1, right: 2, typo: true });
// @ts-expect-error missing app method
app.use(TypedTestContract, { revision: () => 1 });
// @ts-expect-error wrong app result
app.use(TypedTestContract, { confirm: () => "yes", revision: () => 1, updated: () => {} });
app.dispose();
