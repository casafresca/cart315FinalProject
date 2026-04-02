# Optional: Layout Preset Helper

You can also use `DialogueLayoutPresetApplier` if your UI is still looking plain after colors.

## File
- `Assets/Scripts/UI/DialogueLayoutPresetApplier.cs`

## How to use
1. Add `DialogueLayoutPresetApplier` to `DialogueVisualRoot`.
2. Assign these RectTransforms:
- `dialoguePanelRect`
- `notebookBodyRect` (optional)
- `dialogueTextRect`
- `choicesContainerRect`
- `continueButtonRect`
- `choiceCardRects` (choice0..choice3)
3. In component menu, click `Apply Layout Preset`.

## Why this helps
- Makes panel composition intentional
- Creates better spacing for readability
- Stacks choices consistently so it feels designed, not default
