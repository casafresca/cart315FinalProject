using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainSceneGalleryReveal : MonoBehaviour
{
    private static bool sceneHookRegistered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterSceneHook()
    {
        if (sceneHookRegistered)
        {
            return;
        }

        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneHookRegistered = true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!TherapySessionState.TryConsumeGalleryReveal(
                scene.name,
                out Sprite backdropSprite,
                out Color backdropColor,
                out Sprite emptyBoardSprite,
                out Sprite completedBoardSprite,
                out float emptyBoardHoldSeconds,
                out float transitionSeconds,
                out float completedBoardHoldSeconds))
        {
            return;
        }

        GameObject runnerObject = new GameObject("MainSceneGalleryReveal");
        DontDestroyOnLoad(runnerObject);
        MainSceneGalleryReveal runner = runnerObject.AddComponent<MainSceneGalleryReveal>();
        runner.StartCoroutine(runner.PlayReveal(
            backdropSprite,
            backdropColor,
            emptyBoardSprite,
            completedBoardSprite,
            emptyBoardHoldSeconds,
            transitionSeconds,
            completedBoardHoldSeconds));
    }

    private IEnumerator PlayReveal(
        Sprite backdropSprite,
        Color backdropColor,
        Sprite emptyBoardSprite,
        Sprite completedBoardSprite,
        float emptyBoardHoldSeconds,
        float transitionSeconds,
        float completedBoardHoldSeconds)
    {
        Canvas canvas = CreateOverlayCanvas();

        GameObject root = new GameObject("GalleryRevealOverlay", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);

        CanvasGroup rootGroup = root.AddComponent<CanvasGroup>();
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;

        Image backdropImage = CreateFullscreenImage("Backdrop", root.transform, backdropSprite);
        backdropImage.color = backdropSprite != null ? backdropColor : Color.clear;

        Image emptyBoardImage = CreateFullscreenImage("EmptyBoard", root.transform, emptyBoardSprite);
        Image completedBoardImage = CreateFullscreenImage("CompletedBoard", root.transform, completedBoardSprite);
        completedBoardImage.color = new Color(1f, 1f, 1f, 0f);

        if (emptyBoardImage.enabled)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, emptyBoardHoldSeconds));
        }

        if (completedBoardImage.enabled)
        {
            float duration = Mathf.Max(0.01f, transitionSeconds);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                completedBoardImage.color = new Color(1f, 1f, 1f, alpha);
                yield return null;
            }

            completedBoardImage.color = Color.white;
            yield return new WaitForSeconds(Mathf.Max(0f, completedBoardHoldSeconds));
        }

        Destroy(root);
        Destroy(canvas.gameObject);
        Destroy(gameObject);
    }

    private static Canvas CreateOverlayCanvas()
    {
        GameObject canvasObject = new GameObject("GalleryRevealCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static Image CreateFullscreenImage(string name, Transform parent, Sprite sprite)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        Stretch(image.rectTransform);
        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = false;
        image.enabled = sprite != null;
        return image;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
