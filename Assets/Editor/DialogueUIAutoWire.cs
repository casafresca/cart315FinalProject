using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class DialogueUIAutoWire
{
    [MenuItem("Tools/Dialogue/Auto-Wire Styler References")]
    public static void AutoWire()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Dialogue UI Auto-Wire", "No active loaded scene.", "OK");
            return;
        }

        Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Dialogue UI Auto-Wire", "No Canvas found in scene.", "OK");
            return;
        }

        // Ensure a visual root exists so teammates have one predictable place for styling scripts.
        Transform visualRootTf = canvas.transform.Find("DialogueVisualRoot");
        GameObject visualRoot = visualRootTf != null ? visualRootTf.gameObject : new GameObject("DialogueVisualRoot", typeof(RectTransform));
        if (visualRootTf == null)
        {
            Undo.RegisterCreatedObjectUndo(visualRoot, "Create DialogueVisualRoot");
            RectTransform rt = visualRoot.GetComponent<RectTransform>();
            Undo.SetTransformParent(rt, canvas.transform, "Parent DialogueVisualRoot to Canvas");
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.anchoredPosition = Vector2.zero;
        }

        DialogueThemeStyler styler = visualRoot.GetComponent<DialogueThemeStyler>();
        if (styler == null)
        {
            styler = Undo.AddComponent<DialogueThemeStyler>(visualRoot);
        }

        DialogueLayoutPresetApplier layout = visualRoot.GetComponent<DialogueLayoutPresetApplier>();
        if (layout == null)
        {
            layout = Undo.AddComponent<DialogueLayoutPresetApplier>(visualRoot);
        }

        GameObject dialoguePanel = FindByName("DialoguePanel");
        GameObject dialogueText = FindByName("DialogueText");
        GameObject dialogueChoices = FindByName("DialogueChoices");
        GameObject continueButton = FindByName("ContinueButton");

        GameObject[] choices =
        {
            FindByName("choice0"),
            FindByName("choice1"),
            FindByName("choice2"),
            FindByName("choice3")
        };

        Image[] choiceImages = new Image[choices.Length];
        TextMeshProUGUI[] choiceTexts = new TextMeshProUGUI[choices.Length];
        RectTransform[] choiceRects = new RectTransform[choices.Length];

        int choiceCountFound = 0;

        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i] == null) continue;
            choiceCountFound++;

            choiceImages[i] = choices[i].GetComponent<Image>();
            choiceTexts[i] = choices[i].GetComponentInChildren<TextMeshProUGUI>(true);
            choiceRects[i] = choices[i].GetComponent<RectTransform>();

            ChoiceCardHoverFX fx = choices[i].GetComponent<ChoiceCardHoverFX>();
            if (fx == null) fx = Undo.AddComponent<ChoiceCardHoverFX>(choices[i]);

            ChoiceCardAutoReset reset = choices[i].GetComponent<ChoiceCardAutoReset>();
            if (reset == null) reset = Undo.AddComponent<ChoiceCardAutoReset>(choices[i]);

            SerializedObject fxSO = new SerializedObject(fx);
            fxSO.FindProperty("targetRect").objectReferenceValue = choiceRects[i];
            fxSO.FindProperty("cardImage").objectReferenceValue = choiceImages[i];
            fxSO.FindProperty("cardText").objectReferenceValue = choiceTexts[i];
            fxSO.ApplyModifiedPropertiesWithoutUndo();

            Button btn = choices[i].GetComponent<Button>();
            if (btn != null)
            {
                // Adds selected feedback callback each run. If you run repeatedly,
                // remove duplicates manually in Button.OnClick if needed.
                UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, fx.SetSelectedVisual);
                EditorUtility.SetDirty(btn);
            }
        }

        SerializedObject stylerSO = new SerializedObject(styler);
        stylerSO.FindProperty("dialoguePanel").objectReferenceValue = dialoguePanel != null ? dialoguePanel.GetComponent<Image>() : null;
        stylerSO.FindProperty("dialogueText").objectReferenceValue = dialogueText != null ? dialogueText.GetComponent<TextMeshProUGUI>() : null;

        SerializedProperty cardsProp = stylerSO.FindProperty("choiceCards");
        cardsProp.arraySize = choiceImages.Length;
        for (int i = 0; i < choiceImages.Length; i++) cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = choiceImages[i];

        SerializedProperty textsProp = stylerSO.FindProperty("choiceTexts");
        textsProp.arraySize = choiceTexts.Length;
        for (int i = 0; i < choiceTexts.Length; i++) textsProp.GetArrayElementAtIndex(i).objectReferenceValue = choiceTexts[i];

        stylerSO.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject layoutSO = new SerializedObject(layout);
        layoutSO.FindProperty("dialoguePanelRect").objectReferenceValue = dialoguePanel != null ? dialoguePanel.GetComponent<RectTransform>() : null;
        layoutSO.FindProperty("dialogueTextRect").objectReferenceValue = dialogueText != null ? dialogueText.GetComponent<RectTransform>() : null;
        layoutSO.FindProperty("choicesContainerRect").objectReferenceValue = dialogueChoices != null ? dialogueChoices.GetComponent<RectTransform>() : null;
        layoutSO.FindProperty("continueButtonRect").objectReferenceValue = continueButton != null ? continueButton.GetComponent<RectTransform>() : null;

        SerializedProperty rectsProp = layoutSO.FindProperty("choiceCardRects");
        rectsProp.arraySize = choiceRects.Length;
        for (int i = 0; i < choiceRects.Length; i++) rectsProp.GetArrayElementAtIndex(i).objectReferenceValue = choiceRects[i];

        layoutSO.ApplyModifiedPropertiesWithoutUndo();

        styler.ApplyTheme();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = visualRoot;
        EditorGUIUtility.PingObject(visualRoot);

        StringBuilder report = new StringBuilder();
        report.AppendLine("Auto-wire finished.");
        report.AppendLine();
        report.AppendLine($"Canvas: {(canvas != null ? canvas.name : "Missing")}");
        report.AppendLine($"DialogueVisualRoot: {visualRoot.name}");
        report.AppendLine($"DialoguePanel found: {BoolText(dialoguePanel != null)}");
        report.AppendLine($"DialogueText found: {BoolText(dialogueText != null)}");
        report.AppendLine($"DialogueChoices found: {BoolText(dialogueChoices != null)}");
        report.AppendLine($"ContinueButton found: {BoolText(continueButton != null)}");
        report.AppendLine($"Choices found: {choiceCountFound}/4");
        report.AppendLine();
        report.AppendLine("Selected object: DialogueVisualRoot");

        EditorUtility.DisplayDialog("Dialogue UI Auto-Wire", report.ToString(), "OK");
    }

    private static string BoolText(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static GameObject FindByName(string name)
    {
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go.name == name) return go;
        }

        return null;
    }
}
