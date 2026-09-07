// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Configuration;
using System.Data;
using System.Windows;

namespace ReadyCode;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        // The shared FileTreeItem model defers disk-image expansion until after WPF's TreeView
        // has finished generating containers (see FileTreeItem.LoadChildren).
        Models.FileTreeItem.DeferToUiThread = action =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, action);
    }
}
