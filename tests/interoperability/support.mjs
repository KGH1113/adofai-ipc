import assert from "node:assert/strict";
export const pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
export async function until(predicate, milliseconds = 5000) {
  const end = Date.now() + milliseconds;
  while (!predicate() && Date.now() < end) await pause(10);
  assert.ok(predicate(), "condition did not settle");
}
export const value = result => JSON.parse(JSON.stringify(result));
export const handlers = { confirm: ({ message }) => message === "Continue?", revision: () => 7, updated: () => {}, receiveFile: ({ file }) => file.size };
