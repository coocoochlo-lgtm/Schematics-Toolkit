using System.Text.RegularExpressions;

namespace SchematicsAgent;

/// <summary>
/// How much Sprocket needs to read to answer: only what was said to him first, then just the context the request
/// calls for. "Hi" costs a few dozen tokens instead of the whole editor.
/// </summary>
public enum Mode
{
    /// <summary>Greetings, thanks, small talk, questions about Sprocket: the message alone.</summary>
    Chat,
    /// <summary>How things work in general: the message and the toolkit reference.</summary>
    Explain,
    /// <summary>Questions about what's open or selected: the message and the editor state.</summary>
    Look,
    /// <summary>Doing something in the editor: everything, then the plan and the commands.</summary>
    Edit,
}

public static class Modes
{
    private static readonly Regex SmallTalk = new(
        @"^\s*(hi|hiya|hello|hey|heya|yo|sup|howdy|thanks|thank you|thx|ty|ok|okay|cool|nice|great|awesome|lol|good (morning|afternoon|evening|night)|bye|goodbye|see you|how are you|how's it going|what's up|whats up|who are you|what are you|what can you do|what's your name|whats your name)\b[\s!.?,:)]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Doing = new(
        @"\b(add|create|make|build|connect|wire|link|hook up|delete|remove|move|set|change|rename|put|place|spawn|insert|attach|give|replace|duplicate|undo|fix|select|focus|open|arrange|tidy)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Here = new(
        @"\b(this|these|that|here|selected|open|current|my|graph|node|nodes|prefab|scene|object|objects)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Polite = new(
        @"^\s*(\w+,?\s+)?(can|could|would|will) you\b|\bplease\b|\bi (want|need|'d like|would like)\b|\blet'?s\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Asking = new(
        @"^\s*(what|how|why|when|where|which|who|is|are|does|do|can|could|should|explain|tell me|describe)\b|\?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The mode from the words alone, or null when they don't make it clear (the model decides then).</summary>
    public static Mode? ByRule(string utterance)
    {
        bool asking = Asking.IsMatch(utterance), doing = Doing.IsMatch(utterance), here = Here.IsMatch(utterance);
        // "Hi!", "thanks, that's great", "hey Sprocket, how are you?": a greeting with no request in it.
        if (SmallTalk.IsMatch(utterance) && !doing && !here && utterance.Length < 50)
            return Mode.Chat;
        // "Add a timer", and "can you add a timer?" (a request phrased as a question).
        if (doing && (!asking || Polite.IsMatch(utterance)))
            return Mode.Edit;
        if (asking && !doing)
            return here ? Mode.Look : Mode.Explain;
        return null;
    }

    public const string RouterRules =
        "Sort the user's message to Sprocket, an assistant in a Unity visual scripting editor. Answer with one word: " +
        "chat (greetings, thanks, small talk, about Sprocket), explain (how something works in general), " +
        "look (a question about what's open or selected now), edit (asks to create, change, connect or remove something).";

    public const string RouterGrammar = """
root ::= "chat" | "explain" | "look" | "edit"
""";

    public static Mode Parse(string word) => word.Trim().ToLowerInvariant() switch
    {
        "chat" => Mode.Chat,
        "explain" => Mode.Explain,
        "look" => Mode.Look,
        _ => Mode.Edit,
    };
}
