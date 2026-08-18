import assert from "node:assert/strict";
import test from "node:test";
import { decodeResponse, encodeRequest } from "../src/protocol.js";

test("encodes a newline-delimited command", () => {
  assert.equal(encodeRequest("queue.clear"), '{"type":"command","command":"queue.clear"}\n');
});

test("encodes a preset argument without changing the protocol", () => {
  assert.equal(
    encodeRequest("preset.play", "Thanks for watching"),
    '{"type":"command","command":"preset.play","argument":"Thanks for watching"}\n',
  );
});

test("accepts a valid state response", () => {
  const response = decodeResponse(
    '{"type":"response","success":true,"command":"status.announce","state":{"connected":true,"armed":false,"automaticPlayback":false,"queuePaused":false,"englishOnly":true,"queueCount":0}}',
  );

  assert.equal(response.success, true);
  assert.equal(response.state?.englishOnly, true);
});

test("rejects malformed response shapes", () => {
  assert.throws(() => decodeResponse('{"type":"event"}'), /invalid response/);
  assert.throws(
    () => decodeResponse('{"type":"response","success":true,"command":"status.announce","state":{"queueCount":-1}}'),
    /invalid state/,
  );
});
