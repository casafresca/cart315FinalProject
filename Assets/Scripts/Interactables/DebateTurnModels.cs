using System;

[Serializable]
public class DebateChoiceData
{
    public string title = string.Empty;
    public string text = string.Empty;
    public string tone = string.Empty;
    public int insanityDelta;
    public int calmDelta;
}

[Serializable]
public class DebateTurnRequestData
{
    public string role = "soldier";
    public string soldierName = "Soldier";
    public string sceneSummary = string.Empty;
    public string formerIdentity = string.Empty;
    public string militaryRole = string.Empty;
    public string warTheater = string.Empty;
    public string definingTrauma = string.Empty;
    public string triggerStimulus = string.Empty;
    public string identityFracture = string.Empty;
    public string physicalTell = string.Empty;
    public string tabooTopic = string.Empty;
    public int round;
    public int insanity;
    public string insanityStage = string.Empty;
    public string lastPlayerLine = string.Empty;
    public string lastSoldierLine = string.Empty;
    public string[] recentTranscript;
}

[Serializable]
public class DebateTurnResultData
{
    public string soldierReply = string.Empty;
    public string wavPath = string.Empty;
    public string insanityStage = string.Empty;
    public string breakReason = string.Empty;
    public float temperatureUsed;
    public bool peakInsanity;
    public DebateChoiceData[] debateChoices;
}

[Serializable]
public class TypedConversationRequestData
{
    public string role = "soldier";
    public string speakerName = "Soldier";
    public string sceneSummary = string.Empty;
    public string formerIdentity = string.Empty;
    public string militaryRole = string.Empty;
    public string warTheater = string.Empty;
    public string definingTrauma = string.Empty;
    public string triggerStimulus = string.Empty;
    public string identityFracture = string.Empty;
    public string physicalTell = string.Empty;
    public string tabooTopic = string.Empty;
    public int round;
    public int instability;
    public string stage = string.Empty;
    public string requiredWord = string.Empty;
    public string playerTypedLine = string.Empty;
    public string[] offeredWords;
    public string[] detectedTags;
    public string[] recentTranscript;
}

[Serializable]
public class TypedConversationResultData
{
    public string speakerReply = string.Empty;
    public string wavPath = string.Empty;
    public string stateHint = string.Empty;
    public string backstoryReveal = string.Empty;
    public string[] suggestedWords;
    public float temperatureUsed;
    public string stage = string.Empty;
}
