using System;
using System.Collections.Generic;

namespace FlowOS.Agents.Abstractions;

public enum ChatMessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record ChatMessage(
    ChatMessageRole Role,
    string Content,
    string? Name = null,
    DateTimeOffset? Timestamp = null);
