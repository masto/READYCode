// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Rendering;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using ReadyCode.Models;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Tab characters in assembly source and the tab strip's close buttons.
/// </summary>
public class EditorLayoutTests
{
    #region Public Methods

    [AvaloniaFact]
    public void AssemblyTabs_AlignToTabStops_NotAFixedWidth()
    {
        var (window, vm, editor) = Show();
        vm.NewTab(EditorLanguage.Asm);
        Dispatcher.UIThread.RunJobs();
        editor.Document.Text = "a:\tnop\naa:\tnop\naaa:\tnop\n\t\tnop";
        Dispatcher.UIThread.RunJobs();

        var textView = editor.TextArea.TextView;
        textView.EnsureVisualLines();

        // The mnemonic after the tab lands on the next tab stop: column 4 after "a:" and "aa:",
        // column 8 after "aaa:" (which already fills 4), and two tabs reach column 8. A tab of
        // fixed width would have put them at 6, 7, and 8 instead.
        double col = textView.WideSpaceWidth;
        int[] expectedColumns = [4, 4, 8];
        for (int line = 1; line <= 3; line++)
        {
            var visualLine = textView.GetVisualLine(line);
            int tabOffset = editor.Document.GetLineByNumber(line).Offset + line + 1; // after "a:", "aa:", "aaa:"
            int visualColumn = visualLine.GetVisualColumn(tabOffset + 1 - visualLine.StartOffset);
            double x = visualLine.GetTextLineVisualXPosition(visualLine.TextLines[0], visualColumn);
            Assert.Equal(expectedColumns[line - 1] * col, x, 0.5);
        }
        var last = textView.GetVisualLine(4);
        double x2 = last.GetTextLineVisualXPosition(last.TextLines[0], last.GetVisualColumn(2));
        Assert.Equal(8 * col, x2, 0.5);

        RenderCapture.Save(window, "asm-tab-stops.png");
    }

    [AvaloniaFact]
    public void TabCloseButton_ClosesThatTab_EvenWhenInactive()
    {
        var (window, vm, _) = Show();
        var first = vm.ActiveTab!;
        var second = vm.NewTab(EditorLanguage.Asm);
        Dispatcher.UIThread.RunJobs();

        var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tabClose")).ToList();
        Assert.Equal(2, buttons.Count);

        var firstButton = buttons.Single(b => ReferenceEquals(b.DataContext, first));
        firstButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(first, vm.OpenTabs);
        Assert.Same(second, vm.ActiveTab);
        Assert.True(vm.HasClosedTabHistory);
    }

    #endregion

    #region Private Methods

    private static (MainWindow Window, MainViewModel Vm, AvaloniaEdit.TextEditor Editor) Show()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, window.FindControl<AvaloniaEdit.TextEditor>("Editor")!);
    }

    #endregion
}
