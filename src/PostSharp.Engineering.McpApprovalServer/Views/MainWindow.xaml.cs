// Copyright (c) SharpCrafters s.r.o. See the LICENSE.md file in the root directory of this repository root for details.

using System.Windows;

namespace PostSharp.Engineering.McpApprovalServer.Views;

/// <summary>
/// Hidden main window required for WPF application lifecycle.
/// The actual UI is handled through the system tray and approval windows.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
    }
}
