import streamDeck, {
  action,
  type KeyDownEvent,
  SingletonAction,
  type WillAppearEvent,
} from "@elgato/streamdeck";
import { sendSafeSpeakCommand, type SafeSpeakResponse, type SafeSpeakState } from "../protocol.js";

abstract class SafeSpeakCommandAction extends SingletonAction {
  protected abstract readonly command: string;

  override async onWillAppear(event: WillAppearEvent): Promise<void> {
    await this.refreshState(event);
  }

  override async onKeyDown(event: KeyDownEvent): Promise<void> {
    await this.run(event, this.command);
  }

  protected async run(event: KeyDownEvent, command: string, argument?: string): Promise<void> {
    try {
      const response = await sendSafeSpeakCommand(command, argument);
      if (!response.success) {
        streamDeck.logger.error(response.message ?? `SafeSpeak rejected ${command}`);
        await event.action.showAlert();
        return;
      }

      await this.applyState(event, response);
      await event.action.showOk();
    } catch (error) {
      streamDeck.logger.error(error instanceof Error ? error.message : "SafeSpeak action failed");
      await event.action.showAlert();
    }
  }

  private async refreshState(event: WillAppearEvent): Promise<void> {
    try {
      const response = await sendSafeSpeakCommand("status.announce");
      await this.applyState(event, response);
    } catch {
      await event.action.showAlert();
    }
  }

  private async applyState(event: KeyDownEvent | WillAppearEvent, response: SafeSpeakResponse): Promise<void> {
    if (!response.state || !event.action.isKey()) {
      return;
    }

    const enabled = getToggleState(this.command, response.state);
    if (enabled !== undefined) {
      await event.action.setState(enabled ? 1 : 0);
    }
  }
}

function getToggleState(command: string, state: SafeSpeakState): boolean | undefined {
  switch (command) {
    case "tts.toggleArmed":
      return state.armed;
    case "tts.toggleAutomaticPlayback":
      return state.automaticPlayback;
    case "queue.togglePause":
      return state.queuePaused;
    case "moderation.toggleEnglishOnly":
      return state.englishOnly;
    default:
      return undefined;
  }
}

@action({ UUID: "com.bl4ut0.safespeak.toggle-armed" })
export class ToggleArmedAction extends SafeSpeakCommandAction {
  protected override readonly command = "tts.toggleArmed";
}

@action({ UUID: "com.bl4ut0.safespeak.toggle-auto" })
export class ToggleAutomaticPlaybackAction extends SafeSpeakCommandAction {
  protected override readonly command = "tts.toggleAutomaticPlayback";
}

@action({ UUID: "com.bl4ut0.safespeak.play-next" })
export class PlayNextAction extends SafeSpeakCommandAction {
  protected override readonly command = "queue.playNext";
}

@action({ UUID: "com.bl4ut0.safespeak.skip" })
export class SkipCurrentAction extends SafeSpeakCommandAction {
  protected override readonly command = "queue.skipCurrent";
}

@action({ UUID: "com.bl4ut0.safespeak.toggle-pause" })
export class TogglePauseAction extends SafeSpeakCommandAction {
  protected override readonly command = "queue.togglePause";
}

@action({ UUID: "com.bl4ut0.safespeak.clear" })
export class ClearQueueAction extends SafeSpeakCommandAction {
  protected override readonly command = "queue.clear";
}

@action({ UUID: "com.bl4ut0.safespeak.emergency-stop" })
export class EmergencyStopAction extends SafeSpeakCommandAction {
  protected override readonly command = "tts.emergencyStop";
}

@action({ UUID: "com.bl4ut0.safespeak.status" })
export class AnnounceStatusAction extends SafeSpeakCommandAction {
  protected override readonly command = "status.announce";
}

@action({ UUID: "com.bl4ut0.safespeak.cycle-audience" })
export class CycleAudienceAction extends SafeSpeakCommandAction {
  protected override readonly command = "moderation.cycleAudience";
}

@action({ UUID: "com.bl4ut0.safespeak.cycle-strictness" })
export class CycleStrictnessAction extends SafeSpeakCommandAction {
  protected override readonly command = "moderation.cycleStrictness";
}

@action({ UUID: "com.bl4ut0.safespeak.toggle-english" })
export class ToggleEnglishOnlyAction extends SafeSpeakCommandAction {
  protected override readonly command = "moderation.toggleEnglishOnly";
}

type PresetSettings = { message?: string };

@action({ UUID: "com.bl4ut0.safespeak.play-preset" })
export class PlayPresetAction extends SingletonAction<PresetSettings> {
  override async onKeyDown(event: KeyDownEvent<PresetSettings>): Promise<void> {
    const message = event.payload.settings.message?.trim();
    if (!message) {
      await event.action.showAlert();
      return;
    }

    try {
      const response = await sendSafeSpeakCommand("preset.play", message);
      if (response.success) {
        await event.action.showOk();
      } else {
        streamDeck.logger.error(response.message ?? "SafeSpeak rejected the preset");
        await event.action.showAlert();
      }
    } catch (error) {
      streamDeck.logger.error(error instanceof Error ? error.message : "SafeSpeak preset failed");
      await event.action.showAlert();
    }
  }
}
