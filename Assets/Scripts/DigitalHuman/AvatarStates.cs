namespace DataPerson.DigitalHuman
{
    public enum AvatarLocalizationState
    {
        Locating,
        Found,
        Lost,
        Error,
    }

    public enum AvatarConversationState
    {
        Idle,
        Listening,
        Thinking,
        Speaking,
        Error,
    }

    public enum AvatarGesture
    {
        Greeting,
        Yes,
        No,
        Positive,
    }
}
