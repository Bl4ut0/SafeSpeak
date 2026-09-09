/**
 * SafeSpeak Elgato Stream Deck plug-in backend.
 * Exposes only the essential live controls through the loopback service at
 * 127.0.0.1:21214. Detailed safety, voice, source, and theme choices remain
 * in SafeSpeak where their context can be announced accessibly.
 */

const SAFESPEAK_IPC_URL = 'http://127.0.0.1:21214';

let websocket = null;
const activeButtons = new Map(); // context -> { action, state }
let pollInterval = null;

function connectElgatoStreamDeckSocket(inPort, inPluginUUID, inRegisterEvent, inInfo) {
    websocket = new WebSocket("ws://127.0.0.1:" + inPort);

    websocket.onopen = function () {
        websocket.send(JSON.stringify({
            event: inRegisterEvent,
            uuid: inPluginUUID
        }));
        if (!pollInterval) {
            pollInterval = setInterval(pollAllButtons, 1000);
        }
    };

    websocket.onclose = function () {
        if (pollInterval) {
            clearInterval(pollInterval);
            pollInterval = null;
        }
    };

    websocket.onmessage = function (evt) {
        const message = JSON.parse(evt.data);
        const event = message.event;
        const action = message.action;
        const context = message.context;

        if (event === "keyDown") {
            handleKeyDown(action, context);
        } else if (event === "willAppear") {
            activeButtons.set(context, { action: action, state: 0 });
            pollSafeSpeakState(context, action);
        } else if (event === "willDisappear") {
            activeButtons.delete(context);
        }
    };
}

async function sendSafeSpeakCommand(command) {
    try {
        const response = await fetch(`${SAFESPEAK_IPC_URL}/command`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-SafeSpeak-Client': 'streamdeck'
            },
            body: JSON.stringify({ Command: command, Parameter: '' })
        });
        return response.ok ? await response.text() : null;
    } catch (error) {
        console.error("SafeSpeak IPC not reachable:", error);
        return null;
    }
}

async function pollAllButtons() {
    if (activeButtons.size === 0) return;
    try {
        const response = await fetch(`${SAFESPEAK_IPC_URL}/state`);
        if (response.ok) {
            const state = await response.json();
            for (const [context, buttonInfo] of activeButtons.entries()) {
                updateButtonState(context, buttonInfo.action, state);
            }
        }
    } catch (error) {
        // SafeSpeak is not currently running. Stream Deck retains the last state.
    }
}

async function pollSafeSpeakState(context, action) {
    try {
        const response = await fetch(`${SAFESPEAK_IPC_URL}/state`);
        if (response.ok) {
            updateButtonState(context, action, await response.json());
        }
    } catch (error) {
        // SafeSpeak is not currently running. Stream Deck retains the last state.
    }
}

function updateButtonState(context, action, state) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;

    let targetState;
    switch (action) {
        case "com.safespeak.streamdeck.arm":
            targetState = state.IsArmed ? 1 : 0;
            break;
        case "com.safespeak.streamdeck.autoplay":
            targetState = state.IsAutoPlay ? 1 : 0;
            break;
        case "com.safespeak.streamdeck.pause":
            targetState = state.IsPaused ? 1 : 0;
            break;
        default:
            return;
    }

    activeButtons.set(context, { action: action, state: targetState });
    setState(context, targetState);
}

function setState(context, state) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;
    websocket.send(JSON.stringify({
        event: "setState",
        context: context,
        payload: { state: state }
    }));
}

function showSuccess(context) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;
    websocket.send(JSON.stringify({ event: "showOk", context: context }));
}

function showAlert(context) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;
    websocket.send(JSON.stringify({ event: "showAlert", context: context }));
}

async function handleKeyDown(action, context) {
    switch (action) {
        case "com.safespeak.streamdeck.status": {
            const result = await sendSafeSpeakCommand("status");
            if (result) {
                showSuccess(context);
            } else {
                showAlert(context);
            }
            break;
        }

        case "com.safespeak.streamdeck.guidance": {
            if (await sendSafeSpeakCommand("stop_guidance")) {
                showSuccess(context);
            } else {
                showAlert(context);
            }
            break;
        }

        case "com.safespeak.streamdeck.arm": {
            const result = await sendSafeSpeakCommand("toggle_arm");
            if (result) {
                const isArmed = result.trim().toLowerCase() === "armed";
                setState(context, isArmed ? 1 : 0);
                activeButtons.set(context, { action: action, state: isArmed ? 1 : 0 });
                await pollAllButtons();
            } else {
                showAlert(context);
                const prev = activeButtons.get(context);
                if (prev) setState(context, prev.state);
            }
            break;
        }

        case "com.safespeak.streamdeck.panic": {
            if (await sendSafeSpeakCommand("emergency_stop")) {
                showSuccess(context);
                await pollAllButtons();
            } else {
                showAlert(context);
            }
            break;
        }

        case "com.safespeak.streamdeck.autoplay": {
            const result = await sendSafeSpeakCommand("toggle_autoplay");
            if (result) {
                const trimmed = result.trim().toLowerCase();
                if (trimmed === "disarmed") {
                    showAlert(context);
                } else {
                    const isAuto = trimmed === "automaticplaybackenabled";
                    setState(context, isAuto ? 1 : 0);
                    activeButtons.set(context, { action: action, state: isAuto ? 1 : 0 });
                }
                await pollAllButtons();
            } else {
                showAlert(context);
                const prev = activeButtons.get(context);
                if (prev) setState(context, prev.state);
            }
            break;
        }

        case "com.safespeak.streamdeck.pause": {
            const result = await sendSafeSpeakCommand("toggle_pause");
            if (result) {
                const trimmed = result.trim().toLowerCase();
                if (trimmed === "disarmed") {
                    showAlert(context);
                } else {
                    const isPaused = trimmed === "paused";
                    setState(context, isPaused ? 1 : 0);
                    activeButtons.set(context, { action: action, state: isPaused ? 1 : 0 });
                }
                await pollAllButtons();
            } else {
                showAlert(context);
                const prev = activeButtons.get(context);
                if (prev) setState(context, prev.state);
            }
            break;
        }

        case "com.safespeak.streamdeck.next": {
            if (await sendSafeSpeakCommand("speak_next")) {
                showSuccess(context);
                await pollAllButtons();
            } else {
                showAlert(context);
            }
            break;
        }

        case "com.safespeak.streamdeck.skip": {
            if (await sendSafeSpeakCommand("stop_current")) {
                showSuccess(context);
                await pollAllButtons();
            } else {
                showAlert(context);
            }
            break;
        }

        case "com.safespeak.streamdeck.clear": {
            if (await sendSafeSpeakCommand("clear_queue")) {
                showSuccess(context);
                await pollAllButtons();
            } else {
                showAlert(context);
            }
            break;
        }
    }
}
