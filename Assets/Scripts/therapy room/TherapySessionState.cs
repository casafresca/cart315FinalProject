using UnityEngine;

public static class TherapySessionState
{
    public static bool HasOutcome { get; private set; }
    public static bool AnswerWasCorrect { get; private set; }
    public static string SelectedAnswer { get; private set; }
    public static string OutcomeMessage { get; private set; }

    public static bool HasReturnPoint { get; private set; }
    public static string ReturnSceneName { get; private set; }
    public static Vector3 ReturnPosition { get; private set; }
    public static Quaternion ReturnRotation { get; private set; }

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

    public static void ClearOutcome()
    {
        HasOutcome = false;
        AnswerWasCorrect = false;
        SelectedAnswer = string.Empty;
        OutcomeMessage = string.Empty;
    }
}
