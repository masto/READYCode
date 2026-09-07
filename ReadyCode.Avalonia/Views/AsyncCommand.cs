// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Input;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// Minimal <see cref="ICommand"/> over an async delegate, for menu items built in code.
/// </summary>
public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter) => await execute();
}
