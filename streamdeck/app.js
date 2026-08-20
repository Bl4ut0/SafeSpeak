/**
 * SafeSpeak Elgato Stream Deck Plugin Backend
 * Communicates with SafeSpeak local IPC service at 127.0.0.1:21214
 * Handles 2-way synchronization for all logical toggles and cycle actions.
 */

const SAFESPEAK_IPC_URL = 'http://127.0.0.1:21214';

let websocket = null;
let pluginUUID = null;

function connectElgatoStreamDeckSocket(inPort, inPluginUUID, inRegisterEvent, inInfo) {
    pluginUUID = inPluginUUID;
    websocket = new WebSocket("ws://127.0.0.1:" + inPort);

    websocket.onopen = function () {
        const json = {
            event: inRegisterEvent,
            uuid: inPluginUUID
        };
        websocket.send(JSON.stringify(json));
    };

    websocket.onmessage = function (evt) {
        const jsonObj = JSON.parse(evt.data);
        const event = jsonObj['event'];
        const action = jsonObj['action'];
        const context = jsonObj['context'];

        if (event === "keyDown") {
            handleKeyDown(action, context);
        } else if (event === "willAppear") {
            pollSafeSpeakState(context, action);
        }
    };
}

async function sendSafeSpeakCommand(cmd, param = '') {
    try {
        const response = await fetch(`${SAFESPEAK_IPC_URL}/command`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-SafeSpeak-Client': 'streamdeck'
            },
            body: JSON.stringify({ Command: cmd, Parameter: param })
        });
        return await response.text();
    } catch (e) {
        console.error("SafeSpeak IPC not reachable:", e);
        return null;
    }
}

async function pollSafeSpeakState(context, action) {
    try {
        const response = await fetch(`${SAFESPEAK_IPC_URL}/state`);
        if (response.ok) {
            const state = await response.json();
            updateButtonState(context, action, state);
        }
    } catch (e) {
        // SafeSpeak not currently running
    }
}

function updateButtonState(context, action, state) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;

    let targetState = 0;

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
        case "com.safespeak.streamdeck.english":
            targetState = state.EnglishOnly ? 1 : 0;
            break;
        case "com.safespeak.streamdeck.usernames":
            targetState = state.SpeakUsernames ? 1 : 0;
            break;
        case "com.safespeak.streamdeck.aiclassifier":
            targetState = state.AiClassificationEnabled ? 1 : 0;
            break;
        case "com.safespeak.streamdeck.audience":
            setTitle(context, state.AudienceMode || "All");
            return;
        case "com.safespeak.streamdeck.strictness":
            setTitle(context, state.Strictness || "High");
            return;
        case "com.safespeak.streamdeck.connection": targetState = state.IsConnected ? 1 : 0; break;
        case "com.safespeak.streamdeck.chat": targetState = state.AnnounceChatMessages ? 1 : 0; break;
        case "com.safespeak.streamdeck.gifts": targetState = state.AnnounceGifts ? 1 : 0; break;
        case "com.safespeak.streamdeck.follows": targetState = state.AnnounceFollows ? 1 : 0; break;
        case "com.safespeak.streamdeck.shares": targetState = state.AnnounceShares ? 1 : 0; break;
        case "com.safespeak.streamdeck.subscriptions": targetState = state.AnnounceSubscriptions ? 1 : 0; break;
        case "com.safespeak.streamdeck.joins": targetState = state.AnnounceJoins ? 1 : 0; break;
        case "com.safespeak.streamdeck.likes": targetState = state.AnnounceLikes ? 1 : 0; break;
        case "com.safespeak.streamdeck.broadcast": targetState = state.BroadcastOutputEnabled ? 1 : 0; break;
        case "com.safespeak.streamdeck.private": targetState = state.PrivateMonitorEnabled ? 1 : 0; break;
        case "com.safespeak.streamdeck.highcontrast": targetState = state.UseHighContrastTheme ? 1 : 0; break;
    }

    websocket.send(JSON.stringify({
        event: "setState",
        context: context,
        payload: { state: targetState }
    }));
}

function setTitle(context, title) {
    if (!websocket || websocket.readyState !== WebSocket.OPEN) return;
    websocket.send(JSON.stringify({
        event: "setTitle",
        context: context,
        payload: { title: title }
    }));
}

async function handleKeyDown(action, context) {
    const extraToggleCommands = {
        "com.safespeak.streamdeck.connection": "toggle_connection",
        "com.safespeak.streamdeck.chat": "toggle_chat",
        "com.safespeak.streamdeck.gifts": "toggle_gifts",
        "com.safespeak.streamdeck.follows": "toggle_follows",
        "com.safespeak.streamdeck.shares": "toggle_shares",
        "com.safespeak.streamdeck.subscriptions": "toggle_subscriptions",
        "com.safespeak.streamdeck.joins": "toggle_joins",
        "com.safespeak.streamdeck.likes": "toggle_likes",
        "com.safespeak.streamdeck.broadcast": "toggle_broadcast_output",
        "com.safespeak.streamdeck.private": "toggle_private_monitor",
        "com.safespeak.streamdeck.highcontrast": "toggle_high_contrast"
    };
    if (extraToggleCommands[action]) {
        await sendSafeSpeakCommand(extraToggleCommands[action]);
        await pollSafeSpeakState(context, action);
        return;
    }

    switch (action) {
        case "com.safespeak.streamdeck.arm": {
            const res = await sendSafeSpeakCommand("toggle_arm");
            if (res) {
                const isArmed = res.trim().toLowerCase() === "armed";
                websocket.send(JSON.stringify({
                    event: "setState",
                    context: context,
                    payload: { state: isArmed ? 1 : 0 }
                }));
            }
            break;
        }

        case "com.safespeak.streamdeck.autoplay": {
            const res = await sendSafeSpeakCommand("toggle_autoplay");
            if (res) {
                const isAuto = res.trim().toLowerCase() === "autoplayenabled";
                websocket.send(JSON.stringify({
                    event: "setState",
                    context: context,
                    payload: { state: isAuto ? 1 : 0 }
                }));
            }
            break;
        }

        case "com.safespeak.streamdeck.pause": {
            const res = await sendSafeSpeakCommand("toggle_pause");
            if (res) {
                const isPaused = res.trim().toLowerCase() === "paused";
                websocket.send(JSON.stringify({
                    event: "setState",
                    context: context,
                    payload: { state: isPaused ? 1 : 0 }
                }));
            }
            break;
        }

        case "com.safespeak.streamdeck.english": {
            const res = await sendSafeSpeakCommand("toggle_english");
            if (res) {
                const isEnglish = res.trim().toLowerCase() === "englishonlyenabled";
                websocket.send(JSON.stringify({
                    event: "setState",
                    context: context,
                    payload: { state: isEnglish ? 1 : 0 }
                }));
            }
            break;
        }

        case "com.safespeak.streamdeck.usernames": {
            const res = await sendSafeSpeakCommand("toggle_usernames");
            if (res) {
                const isNames = res.trim().toLowerCase() === "usernamesenabled";
                websocket.send(JSON.stringify({
                    event: "setState",
                    context: context,
                    payload: { state: isNames ? 1 : 0 }
                }));
            }
            break;
        }

        case "com.safespeak.streamdeck.aiclassifier": {
            const res = await sendSafeSpeakCommand("toggle_aiclassifier");
            if (res) {
                const isAi = res.trim().toLowerCase() === "aiclassifierenabled";
                websocket.send(JSON.stringify({
                    event: "setState",
                    context: context,
                    payload: { state: isAi ? 1 : 0 }
                }));
            }
            break;
        }

        case "com.safespeak.streamdeck.audience": {
            const newAudience = await sendSafeSpeakCommand("cycle_audience");
            if (newAudience) {
                setTitle(context, newAudience.trim());
            }
            break;
        }

        case "com.safespeak.streamdeck.strictness": {
            const newStrictness = await sendSafeSpeakCommand("cycle_strictness");
            if (newStrictness) {
                setTitle(context, newStrictness.trim());
            }
            break;
        }

        case "com.safespeak.streamdeck.panic":
            await sendSafeSpeakCommand("panic");
            websocket.send(JSON.stringify({ event: "showOk", context: context }));
            break;

        case "com.safespeak.streamdeck.skip":
            await sendSafeSpeakCommand("skip");
            websocket.send(JSON.stringify({ event: "showOk", context: context }));
            break;

        case "com.safespeak.streamdeck.next":
            await sendSafeSpeakCommand("next");
            websocket.send(JSON.stringify({ event: "showOk", context: context }));
            break;

        case "com.safespeak.streamdeck.status":
            await sendSafeSpeakCommand("status");
            break;

        case "com.safespeak.streamdeck.clear":
            await sendSafeSpeakCommand("clear");
            websocket.send(JSON.stringify({ event: "showOk", context: context }));
            break;
    }
}
