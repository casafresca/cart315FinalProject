using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PhotoBoardRevealSetupTool
{
    private const string EmptyBoardAssetPath = "Assets/Art/empty board.png";
    private const string FirstBoardAssetPath = "Assets/Art/Board_soldier_1.png";
    private const string SecondBoardAssetPath = "Assets/Art/board_2.png";

    [MenuItem("Tools/Therapy Room/Setup Photo Board Reveal %#b")]
    public static void SetupPhotoBoardReveal()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Photo Board Reveal", "No active loaded scene.", "OK");
            return;
        }

        PhotoCapture photoCapture = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<PhotoCapture>()
            : null;

        if (photoCapture == null)
        {
            photoCapture = Object.FindFirstObjectByType<PhotoCapture>(FindObjectsInactive.Include);
        }

        if (photoCapture == null)
        {
            EditorUtility.DisplayDialog("Photo Board Reveal", "No PhotoCapture component found in the active scene.", "OK");
            return;
        }

        Canvas canvas = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Photo Board Reveal", "No Canvas found in the active scene.", "OK");
            return;
        }

        GameObject root = FindOrCreateChild(canvas.transform, "BoardRevealRoot");
        RectTransform rootRect = EnsureRectTransform(root);
        StretchToParent(rootRect);

        Image dimmer = root.GetComponent<Image>();
        if (dimmer == null)
        {
            dimmer = Undo.AddComponent<Image>(root);
        }
        dimmer.color = new Color(0f, 0f, 0f, 0.8f);

        GameObject boardBackground = FindOrCreateChild(root.transform, "BoardBackground");
        Image boardBackgroundImage = GetOrAddComponent<Image>(boardBackground);
        RectTransform boardRect = EnsureRectTransform(boardBackground);
        boardRect.anchorMin = new Vector2(0.5f, 0.5f);
        boardRect.anchorMax = new Vector2(0.5f, 0.5f);
        boardRect.pivot = new Vector2(0.5f, 0.5f);
        boardRect.anchoredPosition = Vector2.zero;
        boardRect.sizeDelta = new Vector2(1200f, 700f);
        boardBackgroundImage.preserveAspect = true;

        GameObject developingSlot = FindOrCreateChild(boardBackground.transform, "DevelopingPhotoSlot");
        RectTransform slotRect = EnsureRectTransform(developingSlot);
        slotRect.anchorMin = new Vector2(0.5f, 0.5f);
        slotRect.anchorMax = new Vector2(0.5f, 0.5f);
        slotRect.pivot = new Vector2(0.5f, 0.5f);
        slotRect.anchoredPosition = new Vector2(-292f, 58f);
        slotRect.sizeDelta = new Vector2(180f, 215f);

        GameObject developingPhoto = FindOrCreateChild(developingSlot.transform, "DevelopingPhoto");
        Image developingPhotoImage = GetOrAddComponent<Image>(developingPhoto);
        RectTransform developingRect = EnsureRectTransform(developingPhoto);
        StretchToParent(developingRect);
        developingPhotoImage.preserveAspect = true;
        developingPhotoImage.color = new Color(1f, 1f, 1f, 0f);

        CanvasGroup developingCanvasGroup = developingPhoto.GetComponent<CanvasGroup>();
        if (developingCanvasGroup == null)
        {
            developingCanvasGroup = Undo.AddComponent<CanvasGroup>(developingPhoto);
        }
        developingCanvasGroup.alpha = 0f;

        GameObject helperLabel = FindOrCreateChild(developingSlot.transform, "SlotHelperLabel");
        TextMeshProUGUI helperText = GetOrAddComponent<TextMeshProUGUI>(helperLabel);
        RectTransform helperRect = EnsureRectTransform(helperLabel);
        StretchToParent(helperRect);
        helperText.text = "Move this slot over the empty polaroid";
        helperText.fontSize = 20f;
        helperText.alignment = TextAlignmentOptions.Center;
        helperText.color = new Color(1f, 0.95f, 0.3f, 0.8f);
        helperText.enableWordWrapping = true;

        Sprite emptyBoard = AssetDatabase.LoadAssetAtPath<Sprite>(EmptyBoardAssetPath);
        Sprite firstBoard = AssetDatabase.LoadAssetAtPath<Sprite>(FirstBoardAssetPath);
        Sprite secondBoard = AssetDatabase.LoadAssetAtPath<Sprite>(SecondBoardAssetPath);

        if (emptyBoard != null)
        {
            boardBackgroundImage.sprite = emptyBoard;
        }

        root.SetActive(false);

        SerializedObject so = new SerializedObject(photoCapture);
        so.FindProperty("boardSequenceRoot").objectReferenceValue = root;
        so.FindProperty("boardBackgroundImage").objectReferenceValue = boardBackgroundImage;
        so.FindProperty("developingPhotoImage").objectReferenceValue = developingPhotoImage;
        so.FindProperty("developingPhotoCanvasGroup").objectReferenceValue = developingCanvasGroup;
        so.FindProperty("emptyBoardSprite").objectReferenceValue = emptyBoard;

        SerializedProperty completedBoards = so.FindProperty("completedBoardSprites");
        completedBoards.arraySize = 2;
        completedBoards.GetArrayElementAtIndex(0).objectReferenceValue = firstBoard;
        completedBoards.GetArrayElementAtIndex(1).objectReferenceValue = secondBoard;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(photoCapture);
        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(boardBackground);
        EditorUtility.SetDirty(developingPhoto);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = developingSlot;
        EditorGUIUtility.PingObject(developingSlot);

        StringBuilder report = new StringBuilder();
        report.AppendLine("Photo board reveal setup is ready.");
        report.AppendLine();
        report.AppendLine($"PhotoCapture: {photoCapture.name}");
        report.AppendLine($"Canvas: {canvas.name}");
        report.AppendLine($"BoardRevealRoot created: {root.name}");
        report.AppendLine($"Empty board sprite found: {BoolText(emptyBoard != null)}");
        report.AppendLine($"Board state 1 found: {BoolText(firstBoard != null)}");
        report.AppendLine($"Board state 2 found: {BoolText(secondBoard != null)}");
        report.AppendLine();
        report.AppendLine("Selected object: DevelopingPhotoSlot");
        report.AppendLine("Next step: move DevelopingPhotoSlot so it sits exactly on the empty polaroid position.");

        EditorUtility.DisplayDialog("Photo Board Reveal", report.ToString(), "OK");
    }

    private static string BoolText(bool value)
    {
        return value ? "Yes" : "No";
    }

    private static GameObject FindOrCreateChild(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
        {
            return existing.gameObject;
        }

        GameObject go = new GameObject(objectName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + objectName);
        Undo.SetTransformParent(go.transform, parent, "Parent " + objectName);
        go.transform.localScale = Vector3.one;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localPosition = Vector3.zero;
        return go;
    }

    private static T GetOrAddComponent<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        if (component == null)
        {
            component = Undo.AddComponent<T>(go);
        }

        return component;
    }

    private static RectTransform EnsureRectTransform(GameObject go)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null)
        {
            rect = Undo.AddComponent<RectTransform>(go);
        }

        return rect;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
