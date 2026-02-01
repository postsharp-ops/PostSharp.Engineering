// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System;
using System.Windows;

namespace PostSharp.Engineering.McpApprovalServer.Views;

/// <summary>
/// Code-behind for the history window.
/// </summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        this.InitializeComponent();
        this.Closed += this.OnClosed;
    }

    private void OnClosed( object? sender, EventArgs e )
    {
        // Dispose the ViewModel to unsubscribe from events
        if ( this.DataContext is IDisposable disposable )
        {
            disposable.Dispose();
        }
    }

    private void CloseButton_Click( object sender, RoutedEventArgs e )
    {
        this.Close();
    }
}
