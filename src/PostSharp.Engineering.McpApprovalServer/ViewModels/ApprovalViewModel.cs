// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PostSharp.Engineering.McpApprovalServer.Mcp.Models;
using PostSharp.Engineering.McpApprovalServer.Services;
using System;
using System.Windows.Media;

namespace PostSharp.Engineering.McpApprovalServer.ViewModels;

/// <summary>
/// ViewModel for a single approval request window.
/// </summary>
public partial class ApprovalViewModel : ObservableObject
{
    private readonly ApprovalRequest _request;
    private readonly ApprovalRequestQueue _queue;

    public ApprovalViewModel( ApprovalRequest request, ApprovalRequestQueue queue )
    {
        this._request = request;
        this._queue = queue;
    }

    /// <summary>
    /// Event raised when the window should close.
    /// </summary>
    public event EventHandler? CloseRequested;

    // Request properties
    public string Command => this._request.Command;

    public string ClaimedPurpose => this._request.ClaimedPurpose;

    public string WorkingDirectory => this._request.WorkingDirectory;

    public string? GitBranch => this._request.GitBranch;

    public string ReceivedAt => this._request.ReceivedAt.ToString( "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture );

    // Assessments
    public RiskAssessment CombinedAssessment => this._request.CombinedAssessment;

    public RiskAssessment AiAssessment => this._request.AiAssessment;

    public RiskAssessment RegexAssessment => this._request.RegexAssessment;

    /// <summary>
    /// Gets the AI-generated description of what the command does.
    /// </summary>
    public string CommandDescription => this._request.AiAssessment.Description ?? "No description available";

    // UI helpers
    public bool HasRuleName => !string.IsNullOrEmpty( this._request.RegexAssessment.RuleName );

    public bool IsHighRisk => this._request.CombinedAssessment.Level >= RiskLevel.High;

    public string RiskLevelText => $"Risk Level: {this._request.CombinedAssessment.Level} - {this._request.CombinedAssessment.Recommendation}";

    // Brushes for risk levels
    public Brush RiskLevelBrush => GetRiskBrush( this._request.CombinedAssessment.Level );

    public Brush CombinedRiskBrush => GetRiskBrush( this._request.CombinedAssessment.Level );

    public Brush AiRiskBrush => GetRiskBrush( this._request.AiAssessment.Level );

    public Brush RegexRiskBrush => GetRiskBrush( this._request.RegexAssessment.Level );

    public Brush CombinedRecommendationBrush => GetRecommendationBrush( this._request.CombinedAssessment.Recommendation );

    public Brush AiRecommendationBrush => GetRecommendationBrush( this._request.AiAssessment.Recommendation );

    public Brush RegexRecommendationBrush => GetRecommendationBrush( this._request.RegexAssessment.Recommendation );

    private static Brush GetRiskBrush( RiskLevel level ) => level switch
    {
        RiskLevel.Low => new SolidColorBrush( Color.FromRgb( 76, 175, 80 ) ), // Green
        RiskLevel.Medium => new SolidColorBrush( Color.FromRgb( 255, 152, 0 ) ), // Orange
        RiskLevel.High => new SolidColorBrush( Color.FromRgb( 244, 67, 54 ) ), // Red
        RiskLevel.Critical => new SolidColorBrush( Color.FromRgb( 156, 39, 176 ) ), // Purple
        _ => Brushes.Gray
    };

    private static Brush GetRecommendationBrush( Recommendation recommendation ) => recommendation switch
    {
        Recommendation.Approve => new SolidColorBrush( Color.FromRgb( 76, 175, 80 ) ), // Green
        Recommendation.Reject => new SolidColorBrush( Color.FromRgb( 244, 67, 54 ) ), // Red
        Recommendation.None => Brushes.Gray,
        _ => Brushes.Gray
    };

    [RelayCommand]
    private void Approve()
    {
        this._queue.CompleteRequest( this._request.Id, approved: true );
        this.CloseRequested?.Invoke( this, EventArgs.Empty );
    }

    [RelayCommand]
    private void Reject()
    {
        this._queue.CompleteRequest( this._request.Id, approved: false );
        this.CloseRequested?.Invoke( this, EventArgs.Empty );
    }
}
