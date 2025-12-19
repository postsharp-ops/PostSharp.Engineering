// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.BuildTools.Mcp.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PostSharp.Engineering.BuildTools.Mcp.Services;

/// <summary>
/// Defines the default set of regex-based command rules for risk assessment.
/// </summary>
public static class CommandRules
{
    /// <summary>
    /// Gets the default set of command rules.
    /// Rules are evaluated in order, and the first matching rule is returned.
    /// </summary>
    public static readonly IReadOnlyList<CommandRule> DefaultRules = new[]
    {
        // ============================================
        // Git Operations - Protected Branches
        // ============================================

        new CommandRule
        {
            Name = "git-push-to-protected-branch",
            Pattern = new Regex( @"git\s+push", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Reject,
            Reason = "Direct push to protected branch (main/master/develop/*/release/*) not allowed",
            Condition = ctx => IsProtectedBranch( ctx.CurrentBranch )
        },
        new CommandRule
        {
            Name = "git-force-push",
            Pattern = new Regex( @"git\s+push\s+.*--force", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Force push can destroy commit history"
        },
        new CommandRule
        {
            Name = "git-reset-hard",
            Pattern = new Regex( @"git\s+reset\s+--hard", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Approve,
            Reason = "Hard reset will discard uncommitted changes"
        },
        new CommandRule
        {
            Name = "git-clean-force",
            Pattern = new Regex( @"git\s+clean\s+-[fd]+", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Approve,
            Reason = "Git clean will permanently delete untracked files"
        },
        new CommandRule
        {
            Name = "git-push-tags",
            Pattern = new Regex( @"git\s+push\s+.*--tags", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Medium,
            Recommendation = Recommendation.Approve,
            Reason = "Pushing tags to remote repository"
        },
        new CommandRule
        {
            Name = "git-delete-branch-remote",
            Pattern = new Regex( @"git\s+push\s+.*--delete", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Medium,
            Recommendation = Recommendation.Approve,
            Reason = "Deleting remote branch"
        },
        new CommandRule
        {
            Name = "git-push-to-feature",
            Pattern = new Regex( @"git\s+push", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Low,
            Recommendation = Recommendation.Approve,
            Reason = "Pushing to feature/topic branch"
        },

        // ============================================
        // GitHub CLI Operations
        // ============================================

        new CommandRule
        {
            Name = "gh-repo-delete",
            Pattern = new Regex( @"gh\s+repo\s+delete", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Repository deletion is irreversible and must not be done via MCP"
        },
        new CommandRule
        {
            Name = "gh-release-delete",
            Pattern = new Regex( @"gh\s+release\s+delete", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Reject,
            Reason = "Release deletion should be done through web interface with careful review"
        },
        new CommandRule
        {
            Name = "gh-secret-set",
            Pattern = new Regex( @"gh\s+secret\s+set", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Approve,
            Reason = "Setting repository secrets - ensure credentials are properly secured"
        },
        new CommandRule
        {
            Name = "gh-release-create",
            Pattern = new Regex( @"gh\s+release\s+create", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Medium,
            Recommendation = Recommendation.Approve,
            Reason = "Creating GitHub release"
        },
        new CommandRule
        {
            Name = "gh-pr-merge",
            Pattern = new Regex( @"gh\s+pr\s+merge", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Medium,
            Recommendation = Recommendation.Approve,
            Reason = "Merging pull request"
        },
        new CommandRule
        {
            Name = "gh-pr-create",
            Pattern = new Regex( @"gh\s+pr\s+create", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Low,
            Recommendation = Recommendation.Approve,
            Reason = "Creating pull request"
        },

        // ============================================
        // File Operations - ALL FORBIDDEN
        // ============================================

        new CommandRule
        {
            Name = "delete-git-directory",
            Pattern = new Regex( @"(Remove-Item|del|rm|rmdir).*\.git", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Deleting .git directory would destroy repository history"
        },
        new CommandRule
        {
            Name = "format-drive",
            Pattern = new Regex( @"Format-", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Drive formatting is catastrophically destructive"
        },
        new CommandRule
        {
            Name = "remove-item-recurse",
            Pattern = new Regex( @"Remove-Item.*-Recurse", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Recursive file deletion must be performed in the container, not on host"
        },
        new CommandRule
        {
            Name = "file-deletion",
            Pattern = new Regex( @"(Remove-Item|del\s|rm\s|rmdir)", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "File operations must be performed in the container, not on host"
        },
        new CommandRule
        {
            Name = "file-write-operations",
            Pattern = new Regex( @"(Set-Content|Out-File|New-Item.*-ItemType\s+File|Copy-Item|Move-Item)", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Reject,
            Reason = "File modifications must be performed in the container"
        },
        new CommandRule
        {
            Name = "directory-operations",
            Pattern = new Regex( @"(New-Item.*-ItemType\s+Directory|mkdir)", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Reject,
            Reason = "Directory operations must be performed in the container"
        },

        // ============================================
        // Package Publishing - ALL FORBIDDEN
        // ============================================

        new CommandRule
        {
            Name = "nuget-push",
            Pattern = new Regex( @"dotnet\s+nuget\s+push", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Package publishing must be done through CI/CD pipeline, not manually"
        },
        new CommandRule
        {
            Name = "npm-publish",
            Pattern = new Regex( @"npm\s+publish", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Package publishing must be done through CI/CD pipeline"
        },
        new CommandRule
        {
            Name = "docker-push",
            Pattern = new Regex( @"docker\s+push", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Reject,
            Reason = "Docker image publishing must be done through CI/CD pipeline"
        },

        // ============================================
        // Network/External Access - Download-Execute
        // ============================================

        new CommandRule
        {
            Name = "curl-download-execute",
            Pattern = new Regex( @"curl.*\|\s*(bash|sh|pwsh|powershell)", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Download-and-execute pattern is a common malware delivery method"
        },
        new CommandRule
        {
            Name = "wget-execute",
            Pattern = new Regex( @"wget.*&&\s*\./", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Download-and-execute pattern is a common malware delivery method"
        },
        new CommandRule
        {
            Name = "invoke-webrequest-execute",
            Pattern = new Regex( @"Invoke-WebRequest.*\|\s*Invoke-Expression", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Critical,
            Recommendation = Recommendation.Reject,
            Reason = "Download-and-execute pattern is a common malware delivery method"
        },

        // ============================================
        // Credential/Secret Access
        // ============================================

        new CommandRule
        {
            Name = "read-credentials-json",
            Pattern = new Regex( @"Get-Content.*credentials\.json", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Reject,
            Reason = "Reading credential files may indicate data exfiltration attempt"
        },
        new CommandRule
        {
            Name = "read-env-file",
            Pattern = new Regex( @"Get-Content.*\.env", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.High,
            Recommendation = Recommendation.Reject,
            Reason = "Reading .env files may indicate secrets exfiltration attempt"
        },
        new CommandRule
        {
            Name = "export-env-variables",
            Pattern = new Regex( @"\$env:\w+\s*=", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Low,
            Recommendation = Recommendation.None,
            Reason = "Setting environment variables - deferring to AI to determine if secrets are exposed"
        },
        new CommandRule
        {
            Name = "env-var-reference-powershell",
            Pattern = new Regex( @"\$env:\w+", RegexOptions.IgnoreCase ),
            RiskLevel = RiskLevel.Low,
            Recommendation = Recommendation.None,
            Reason = "Environment variable reference detected - deferring to AI to determine if leaked"
        },
        new CommandRule
        {
            Name = "env-var-reference-bash",
            Pattern = new Regex( @"\$\{?\w+\}?", RegexOptions.None ),
            RiskLevel = RiskLevel.Low,
            Recommendation = Recommendation.None,
            Reason = "Potential environment variable reference detected - deferring to AI analysis"
        }
    };

    /// <summary>
    /// Checks if a branch name matches protected branch patterns.
    /// Protected branches: main, master, develop/*, release/*
    /// </summary>
    private static bool IsProtectedBranch( string? branch )
    {
        if ( branch == null )
        {
            return false;
        }

        return branch == "main" ||
               branch == "master" ||
               branch.StartsWith( "develop/", StringComparison.Ordinal ) ||
               branch.StartsWith( "release/", StringComparison.Ordinal );
    }
}