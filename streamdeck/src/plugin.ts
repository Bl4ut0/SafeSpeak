import streamDeck from "@elgato/streamdeck";
import {
  AnnounceStatusAction,
  ClearQueueAction,
  CycleAudienceAction,
  CycleStrictnessAction,
  EmergencyStopAction,
  PlayNextAction,
  PlayPresetAction,
  SkipCurrentAction,
  ToggleArmedAction,
  ToggleAutomaticPlaybackAction,
  ToggleEnglishOnlyAction,
  TogglePauseAction,
} from "./actions/safespeak-actions.js";

streamDeck.actions.registerAction(new ToggleArmedAction());
streamDeck.actions.registerAction(new ToggleAutomaticPlaybackAction());
streamDeck.actions.registerAction(new PlayNextAction());
streamDeck.actions.registerAction(new SkipCurrentAction());
streamDeck.actions.registerAction(new TogglePauseAction());
streamDeck.actions.registerAction(new ClearQueueAction());
streamDeck.actions.registerAction(new EmergencyStopAction());
streamDeck.actions.registerAction(new AnnounceStatusAction());
streamDeck.actions.registerAction(new CycleAudienceAction());
streamDeck.actions.registerAction(new CycleStrictnessAction());
streamDeck.actions.registerAction(new ToggleEnglishOnlyAction());
streamDeck.actions.registerAction(new PlayPresetAction());

streamDeck.connect();
