public static class TherapySessionState
{
    public static bool HasOutcome { get; private set; }
    public static bool AnswerWasCorrect { get; private set; }
    public static string SelectedAnswer { get; private set; }
    public static string OutcomeMessage { get; private set; }

    public static void SetOutcome(bool answerWasCorrect, string selectedAnswer)
    {
        HasOutcome = true;
        AnswerWasCorrect = answerWasCorrect;
        SelectedAnswer = selectedAnswer;
        OutcomeMessage = answerWasCorrect
            ? "The NPC survived because you chose the correct answer."
            : "The NPC died because you chose the wrong answer.";
    }

    public static void ClearOutcome()
    {
        HasOutcome = false;
        AnswerWasCorrect = false;
        SelectedAnswer = string.Empty;
        OutcomeMessage = string.Empty;
    }
}
