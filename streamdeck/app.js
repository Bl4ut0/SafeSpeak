/**
 * SafeSpeak Elgato Stream Deck plug-in backend.
 * Exposes only the essential live controls through the loopback service at
 * 127.0.0.1:21214. Detailed safety, voice, source, and theme choices remain
 * in SafeSpeak where their context can be announced accessibly.
 */

const SAFESPEAK_IPC_URL = 'http://127.0.0.1:21214';

let websocket = null;

function connectElgatoStreamDeckSocket(inPort, inPluginUUID, inRegisterEvent, inInfo) {
    websocket = new WebSocket("ws://127.0.0.1:" + inPort);

    websocket.onopen = function () {
        websocket.send(JSON.stringify({
            event: inRegisterEvent,
            uuid: inPluginUUID
        }));
    };

    websocket.onmessage = function (evt) {
        const message = JSON.parse(evt.data);
        const event = message.event;
        const action = message.action;
        const context = message.context;

        if (event === "keyDown") {
            handleKeyDown(action, context);
        } else if (event === "willAppear") {
            pollSafeSpeakState(context, action);
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

async function handleKeyDown(action, context) {
    switch (action) {
        case "com.safespeak.streamdeck.status":
            await sendSafeSpeakCommand("status");
            break;

        case "com.safespeak.streamdeck.arm": {
            const result = await sendSafeSpeakCommand("toggle_arm");
            if (result) setState(context, result.trim().toLowerCase() === "armed" ? 1 : 0);
            break;
        }

        case "com.safespeak.streamdeck.panic":
            if (await sendSafeSpeakCommand("emergency_stop")) showSuccess(context);
            break;

        case "com.safespeak.streamdeck.autoplay": {
            const result = await sendSafeSpeakCommand("toggle_autoplay");
            if (result) {
                setState(context, result.trim().toLowerCase() === "automaticplaybackenabled" ? 1 : 0);
            }
            break;
        }

        case "com.safespeak.streamdeck.pause": {
            const result = await sendSafeSpeakCommand("toggle_pause");
            if (result) setState(context, result.trim().toLowerCase() === "paused" ? 1 : 0);
            break;
        }

        case "com.safespeak.streamdeck.next":
            if (await sendSafeSpeakCommand("speak_next")) showSuccess(context);
            break;

        case "com.safespeak.streamdeck.skip":
            if (await sendSafeSpeakCommand("stop_current")) showSuccess(context);
            break;

        case "com.safespeak.streamdeck.clear":
            if (await sendSafeSpeakCommand("clear_queue")) showSuccess(context);
            break;
    }
}
