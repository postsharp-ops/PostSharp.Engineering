// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using PostSharp.Engineering.McpApprovalServer.ViewModels;
using System;
using System.Windows;

namespace PostSharp.Engineering.McpApprovalServer.Views;

/// <summary>
/// Window for displaying and handling a single approval request.
/// </summary>
public partial class ApprovalWindow : Window
{
    public ApprovalWindow()
    {
        this.InitializeComponent();
        this.DataContextChanged += this.OnDataContextChanged;
    }

    private void OnDataContextChanged( object sender, DependencyPropertyChangedEventArgs e )
    {
        if ( e.OldValue is ApprovalViewModel oldVm )
        {
            oldVm.CloseRequested -= this.OnCloseRequested;
        }

        if ( e.NewValue is ApprovalViewModel newVm )
        {
            newVm.CloseRequested += this.OnCloseRequested;
        }
    }

    private void OnCloseRequested( object? sender, EventArgs e )
    {
        this.Close();
    }

    protected override void OnClosed( EventArgs e )
    {
        if ( this.DataContext is ApprovalViewModel vm )
        {
            vm.CloseRequested -= this.OnCloseRequested;
        }

        base.OnClosed( e );
    }
}
