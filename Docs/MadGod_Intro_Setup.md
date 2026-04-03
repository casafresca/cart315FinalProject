# Mad God Intro Setup (3D Environment Scene)

## Scripts

- `Assets/Scripts/Interactables/MadGodIntroController.cs`
- `Assets/Scripts/Interactables/MadGodCompanion.cs`

## Goal flow

1. Player walks 5 steps away from gate.
2. Mad God rushes toward player.
3. Player controls lock.
4. Pre-recorded intro WAV lines play.
5. Mad God retreats to mansion marker.
6. Player controls unlock.

## Scene wiring

1. Put `MadGodIntroController` on Mad God root GameObject.
2. Assign `player` (Player transform).
3. Assign `gateTransform` (gate marker transform).
4. Assign `retreatTarget` (empty GameObject near mansion entry).
5. Add an `AudioSource` to Mad God and assign it to `voiceSource`.
6. Drag intro clips (`madGod1.wav`, etc.) into `introClips`.
7. Keep `triggerDistanceFromGate = 5` (or tune).
8. Keep `rushSpeed` high (example 16).
9. Keep `autoFindInputAndWeapon = true` unless you want manual control lock list.

## Optional future interaction

- Add `MadGodCompanion` to Mad God.
- Set `promptMessage` in Inspector (from `Interactable`) if you want look-at prompt text.
- Toggle:
  - `usePreRecordedClips`
  - `useTTSReply`

This gives a safe placeholder for future key-triggered talk without adding soldier combat logic.
## Talking Sprite Setup

1. Add `MadGodTalkSpriteAnimator` to the Mad God object (or sprite child).
2. Assign:
- `targetRenderer` -> Mad God SpriteRenderer
- `mouthClosedSprite` -> closed-mouth sprite
- `mouthOpenSprite` -> open-mouth sprite
3. If using intro WAV clips, assign `voiceSource` (same AudioSource as intro controller).
4. Keep `listenToVoiceSource = true` for pre-recorded intro playback.
5. Keep `listenToTTSRunner = true` for AI TTS playback.
6. Tune `switchInterval` around `0.07-0.11` for natural lip flapping.

The mouth animation will auto-run while audio is playing and return to closed mouth when speech ends.
