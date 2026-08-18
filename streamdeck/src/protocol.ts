import { createConnection } from "node:net";

const pipePath = String.raw`\\.\pipe\SafeSpeakControl`;
const maximumResponseBytes = 64 * 1024;
let requestQueue: Promise<void> = Promise.resolve();

export type SafeSpeakState = {
  connected: boolean;
  armed: boolean;
  automaticPlayback: boolean;
  queuePaused: boolean;
  englishOnly: boolean;
  queueCount: number;
};

export type SafeSpeakResponse = {
  type: "response";
  success: boolean;
  command: string;
  message?: string;
  state?: SafeSpeakState;
};

export function encodeRequest(command: string, argument?: string): string {
  return `${JSON.stringify({ type: "command", command, ...(argument === undefined ? {} : { argument }) })}\n`;
}

export function decodeResponse(line: string): SafeSpeakResponse {
  const value: unknown = JSON.parse(line);
  if (!isObject(value) || value.type !== "response" || typeof value.success !== "boolean" || typeof value.command !== "string") {
    throw new Error("SafeSpeak returned an invalid response");
  }

  if (value.message !== undefined && typeof value.message !== "string") {
    throw new Error("SafeSpeak returned an invalid message");
  }

  if (value.state !== undefined && !isSafeSpeakState(value.state)) {
    throw new Error("SafeSpeak returned an invalid state");
  }

  return value as SafeSpeakResponse;
}

export function sendSafeSpeakCommand(command: string, argument?: string): Promise<SafeSpeakResponse> {
  const operation = requestQueue.then(() => sendCommandNow(command, argument));
  requestQueue = operation.then(
    () => undefined,
    () => undefined,
  );
  return operation;
}

function sendCommandNow(command: string, argument?: string): Promise<SafeSpeakResponse> {
  return new Promise((resolve, reject) => {
    const socket = createConnection(pipePath);
    let buffer = "";
    let settled = false;

    const fail = (error: Error): void => {
      if (settled) {
        return;
      }

      settled = true;
      socket.destroy();
      reject(error);
    };

    socket.setEncoding("utf8");
    socket.setTimeout(2000);
    socket.once("connect", () => socket.write(encodeRequest(command, argument)));
    socket.on("data", (chunk: string) => {
      buffer += chunk;
      if (Buffer.byteLength(buffer, "utf8") > maximumResponseBytes) {
        fail(new Error("SafeSpeak response exceeded the safety limit"));
        return;
      }

      const newlineIndex = buffer.indexOf("\n");
      if (newlineIndex < 0 || settled) {
        return;
      }

      try {
        const response = decodeResponse(buffer.slice(0, newlineIndex));
        settled = true;
        socket.end();
        resolve(response);
      } catch (error) {
        fail(error instanceof Error ? error : new Error("SafeSpeak response could not be read"));
      }
    });
    socket.once("timeout", () => fail(new Error("SafeSpeak did not respond")));
    socket.once("error", (error) => fail(error));
    socket.once("close", () => {
      if (!settled) {
        fail(new Error("SafeSpeak is not running"));
      }
    });
  });
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isSafeSpeakState(value: unknown): value is SafeSpeakState {
  if (!isObject(value)) {
    return false;
  }

  return typeof value.connected === "boolean" &&
    typeof value.armed === "boolean" &&
    typeof value.automaticPlayback === "boolean" &&
    typeof value.queuePaused === "boolean" &&
    typeof value.englishOnly === "boolean" &&
    typeof value.queueCount === "number" &&
    Number.isInteger(value.queueCount) &&
    value.queueCount >= 0;
}
