// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows.Input;

namespace ReadyCode.Avalonia.Views;

/// <summary>
/// Minimal <see cref="ICommand"/> over an async delegate, for menu items built in code (context
/// menus). Menu items declared in XAML use Click handlers instead, so this deliberately has no
/// canExecute support - there is nothing to disable.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    #region Private Fields

    private readonly Func<Task> _execute;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncCommand"/> class.
    /// </summary>
    /// <param name="execute">The action the command runs.</param>
    public AsyncCommand(Func<Task> execute) => _execute = execute;

    #endregion

    #region Public Events

    /// <summary>
    /// Occurs when the command's ability to execute changes. Never raised: this command is always
    /// executable.
    /// </summary>
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns whether the command can execute. Always true.
    /// </summary>
    /// <param name="parameter">Unused.</param>
    public bool CanExecute(object? parameter) => true;

    /// <summary>
    /// Runs the command's action.
    /// </summary>
    /// <param name="parameter">Unused.</param>
    public async void Execute(object? parameter) => await _execute();

    #endregion
}
