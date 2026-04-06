using UnityEngine;

public static class TherapySessionState
{
    public static bool HasOutcome { get; private set; }
    public static bool AnswerWasCorrect { get; private set; }
    public static string SelectedAnswer { get; private set; }
    public static string OutcomeMessage { get; private set; }
    public static int CapturedSoldierCount { get; private set; }

    public static bool HasReturnPoint { get; private set; }
    public static string ReturnSceneName { get; private set; }
    public static Vector3 ReturnPosition { get; private set; }
    public static Quaternion ReturnRotation { get; private set; }
    public static bool HasCompletedMadGodIntro { get; private set; }
    public static bool HasPendingGalleryReveal { get; private set; }
    public static string GalleryRevealSceneName { get; private set; }
    public static Sprite GalleryEmptyBoardSprite { get; private set; }
    public static Sprite GalleryCompletedBoardSprite { get; private set; }
    public static Sprite GalleryBackdropSprite { get; private set; }
    public static Color GalleryBackdropColor { get; private set; }
    public static float GalleryEmptyBoardHoldSeconds { get; private set; }
    public static float GalleryTransitionSeconds { get; private set; }
    public static float GalleryCompletedBoardHoldSeconds { get; private set; }

    public static void SetOutcome(bool answerWasCorrect, string selectedAnswer)
    {
        HasOutcome = true;
        AnswerWasCorrect = answerWasCorrect;
        SelectedAnswer = selectedAnswer;
        OutcomeMessage = answerWasCorrect
            ? "The NPC survived because you chose the correct answer."
            : "The NPC died because you chose the wrong answer.";
    }

    public static void SetReturnPoint(string sceneName, Vector3 position, Quaternion rotation)
    {
        HasReturnPoint = true;
        ReturnSceneName = sceneName;
        ReturnPosition = position;
        ReturnRotation = rotation;
    }

    public static bool TryConsumeReturnPoint(string targetSceneName, out Vector3 position, out Quaternion rotation)
    {
        if (!HasReturnPoint || !string.Equals(ReturnSceneName, targetSceneName, System.StringComparison.Ordinal))
        {
            position = default;
            rotation = default;
            return false;
        }

        position = ReturnPosition;
        rotation = ReturnRotation;
        HasReturnPoint = false;
        ReturnSceneName = string.Empty;
        ReturnPosition = default;
        ReturnRotation = default;
        return true;
    }

    public static void QueueGalleryReveal(
        string sceneName,
        Sprite backdropSprite,
        Color backdropColor,
        Sprite emptyBoardSprite,
        Sprite completedBoardSprite,
        float emptyBoardHoldSeconds,
        float transitionSeconds,
        float completedBoardHoldSeconds)
    {
        HasPendingGalleryReveal = true;
        GalleryRevealSceneName = sceneName;
        GalleryBackdropSprite = backdropSprite;
        GalleryBackdropColor = backdropColor;
        GalleryEmptyBoardSprite = emptyBoardSprite;
        GalleryCompletedBoardSprite = completedBoardSprite;
        GalleryEmptyBoardHoldSeconds = emptyBoardHoldSeconds;
        GalleryTransitionSeconds = transitionSeconds;
        GalleryCompletedBoardHoldSeconds = completedBoardHoldSeconds;
    }

    public static bool TryConsumeGalleryReveal(
        string targetSceneName,
        out Sprite backdropSprite,
        out Color backdropColor,
        out Sprite emptyBoardSprite,
        out Sprite completedBoardSprite,
        out float emptyBoardHoldSeconds,
        out float transitionSeconds,
        out float completedBoardHoldSeconds)
    {
        if (!HasPendingGalleryReveal || !string.Equals(GalleryRevealSceneName, targetSceneName, System.StringComparison.Ordinal))
        {
            backdropSprite = null;
            backdropColor = Color.clear;
            emptyBoardSprite = null;
            completedBoardSprite = null;
            emptyBoardHoldSeconds = 0f;
            transitionSeconds = 0f;
            completedBoardHoldSeconds = 0f;
            return false;
        }

        backdropSprite = GalleryBackdropSprite;
        backdropColor = GalleryBackdropColor;
        emptyBoardSprite = GalleryEmptyBoardSprite;
        completedBoardSprite = GalleryCompletedBoardSprite;
        emptyBoardHoldSeconds = GalleryEmptyBoardHoldSeconds;
        transitionSeconds = GalleryTransitionSeconds;
        completedBoardHoldSeconds = GalleryCompletedBoardHoldSeconds;

        HasPendingGalleryReveal = false;
        GalleryRevealSceneName = string.Empty;
        GalleryBackdropSprite = null;
        GalleryBackdropColor = Color.clear;
        GalleryEmptyBoardSprite = null;
        GalleryCompletedBoardSprite = null;
        GalleryEmptyBoardHoldSeconds = 0f;
        GalleryTransitionSeconds = 0f;
        GalleryCompletedBoardHoldSeconds = 0f;
        return true;
    }

    public static void ClearOutcome()
    {
        HasOutcome = false;
        AnswerWasCorrect = false;
        SelectedAnswer = string.Empty;
        OutcomeMessage = string.Empty;
    }

    public static void MarkMadGodIntroCompleted()
    {
        HasCompletedMadGodIntro = true;
    }

    public static int RegisterCapturedSoldier()
    {
        CapturedSoldierCount++;
        return CapturedSoldierCount;
    }
}
