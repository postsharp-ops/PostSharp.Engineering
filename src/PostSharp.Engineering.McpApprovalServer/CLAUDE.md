# MCP Approval Server

A WPF GUI application that provides an MCP (Model Context Protocol) server for executing privileged commands on the host machine with human approval. Used when Claude runs inside a Docker container and needs to perform operations requiring host credentials or external access.

## Architecture

### Approval Flow

1. **Request Reception**: Claude in Docker calls `ExecuteCommand` via MCP
2. **Parallel Risk Analysis**: Two analyzers run concurrently:
   - **Regex Rule Engine** (`RegexRuleEngine.cs`): Fast pattern matching against predefined rules
   - **AI Risk Analyzer** (`RiskAnalyzer.cs`): Uses Claude CLI (Haiku, escalating to Opus if uncertain)
3. **Risk Combination** (`RiskCombiner.cs`): Takes the maximum (most restrictive) of both assessments
4. **Human Approval** (`GuiApprovalPrompter.cs`): Shows approval dialog with risk assessment
5. **Execution** (`CommandExecutor.cs`): If approved, executes via PowerShell on host
6. **History Recording** (`CommandHistoryService.cs`): Records for session-based auto-approval

### Risk Levels

| Level | Description |
|-------|-------------|
| `Low` | Safe operations, recommendation shown |
| `Medium` | Requires attention, manual review expected |
| `High` | Potentially dangerous, careful review required |
| `Critical` | Very dangerous, default recommendation is reject |
| `Uncertain` | AI couldn't determine - escalates to Opus, then human |

### Key Components

```
Mcp/
├── Models/
│   ├── CommandContext.cs      # Git context for rule evaluation
│   ├── CommandRecord.cs       # History entry
│   ├── CommandResult.cs       # Execution result
│   ├── CommandRule.cs         # Rule definition
│   └── RiskAssessment.cs      # Risk analysis result
├── Services/
│   ├── CommandRules.cs        # Predefined regex rules
│   ├── CommandExecutor.cs     # PowerShell execution
│   ├── CommandHistoryService.cs # Session history
│   ├── RegexRuleEngine.cs     # Pattern-based risk evaluation
│   ├── RiskAnalyzer.cs        # AI-based risk analysis
│   └── RiskCombiner.cs        # Combines AI + regex assessments
└── Tools/
    └── ExecuteCommandTool.cs  # MCP tool entry point

Services/
├── ApprovalRequest.cs         # Approval request model
├── ApprovalRequestQueue.cs    # Concurrent request queue
├── GitHelper.cs               # Git operations helper
├── GuiApprovalPrompter.cs     # WPF approval dialog integration
├── McpHttpServer.cs           # HTTP server for MCP
├── TraceLogger.cs             # File-based logging
└── TrayIconService.cs         # System tray integration

Views/
├── ApprovalWindow.xaml        # Approval dialog
├── HistoryWindow.xaml         # Command history viewer
└── MainWindow.xaml            # Main window (hidden, tray-based)
```

## Adding Command Rules

Rules are defined in `Mcp/Services/CommandRules.cs`. Each rule has:

- `Name`: Unique identifier for logging
- `Pattern`: Regex to match against the command
- `RiskLevel`: Low, Medium, High, or Critical
- `Recommendation`: Approve, Reject, or None (defer to AI)
- `Reason`: Human-readable explanation
- `Condition`: Optional function for context-aware matching (e.g., branch checks)

Example:
```csharp
new CommandRule
{
    Name = "git-push-to-feature",
    Pattern = new Regex( @"git\s+push", RegexOptions.IgnoreCase ),
    RiskLevel = RiskLevel.Low,
    Recommendation = Recommendation.Approve,
    Reason = "Pushing to feature/topic branch"
}
```

Rules are evaluated in order - first match wins. Place more specific rules before general ones.

## AI Risk Analysis

The `RiskAnalyzer` uses Claude CLI with a two-tier approach:

1. **Haiku first**: Fast, cheap analysis for most requests
2. **Opus escalation**: If Haiku returns `UNCERTAIN`, escalates to Opus
3. **Human fallback**: If Opus is also uncertain, defaults to Medium risk

The AI receives:
- Command and claimed purpose
- Working directory and git branch
- Session history (last 10 commands)
- For `git push`: Full commit diff for secrets/content analysis

## Session Auto-Approval

Commands that were previously approved in the same session can be auto-approved:
- Same command text
- Same working directory
- Recorded in `CommandHistoryService`

This allows repetitive operations (like multiple pushes) without repeated approval dialogs.

## Security Considerations

- Server binds to `localhost:9847` only (not network-exposed)
- Docker accesses via host gateway IP
- All commands require human approval (no automatic execution)
- Critical operations (nuget push, repo delete, force push) default to reject
- AI analyzes commit diffs for secrets, credentials, inappropriate content
- File operations on host are forbidden (must be done in container)
