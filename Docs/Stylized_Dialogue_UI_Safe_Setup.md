# Stylized Dialogue UI (Safe Additive Setup)

This setup avoids changing core gameplay scripts and scene logic.

## Added scripts
- `Assets/Scripts/UI/DialogueThemeStyler.cs`
- `Assets/Scripts/UI/ChoiceCardHoverFX.cs`
- `Assets/Scripts/UI/ChoiceCardAutoReset.cs`

## Unity hookup (safe)
1. Create a new empty UI parent under Canvas: `DialogueVisualRoot`.
2. Add `DialogueThemeStyler` to `DialogueVisualRoot`.
3. Assign references in `DialogueThemeStyler`:
- `dialoguePanel` -> your existing `DialoguePanel` image
- `dialogueText` -> your existing `DialogueText` TMP
- `choiceCards` -> choice0..choice3 Image components
- `choiceTexts` -> each choice TMP text
- Optional: `dimBackdrop`, `notebookBody`, `polaroidFrame`, `labelTexts`
4. On each choice button object (`choice0..choice3`):
- Add `ChoiceCardHoverFX`
- Add `ChoiceCardAutoReset`
- Assign `cardImage` and `cardText` on `ChoiceCardHoverFX`
5. In each choice button `OnClick()` list, add a call to:
- `ChoiceCardHoverFX.SetSelectedVisual()`

## Notes
- This does not modify `DialogueManager` behavior.
- If your team reworks dialogue logic, these visual scripts still remain isolated.
