// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using ReadyCode.Avalonia.ViewModels;
using ReadyCode.Avalonia.Views;
using Xunit;

namespace ReadyCode.Avalonia.Tests;

/// <summary>
/// Tests for the find/replace bar and the window's search, navigation, and replace handling.
/// </summary>
public class FindReplaceTests
{
    #region Public Methods

    [AvaloniaFact]
    public void Find_CountsAndNavigatesMatches_ThenReplaceAll()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 400 };
        window.Show();
        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        var bar = window.FindControl<FindBarControl>("FindBar")!;
        editor.Document.Text = "10 PRINT \"HELLO\"\n20 PRINT \"HELLO AGAIN\"\n30 GOTO 10";
        editor.CaretOffset = 0;
        Dispatcher.UIThread.RunJobs();

        Assert.False(bar.IsVisible);
        window.KeyPressQwerty(PhysicalKey.F, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.True(bar.IsVisible);

        var search = bar.FindControl<TextBox>("SearchBox")!;
        var count = bar.FindControl<TextBlock>("MatchCountText")!;
        search.Text = "hello";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1 of 2", count.Text);
        RenderCapture.Save(window, "find-bar.png");

        // Enter in the search box = next match; the match becomes the selection.
        search.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("2 of 2", count.Text);
        Assert.Equal("HELLO", editor.SelectedText);
        Assert.Equal(27, editor.SelectionStart);

        bar.FindControl<ToggleButton>("MatchCaseBtn")!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("No results", count.Text);
        bar.FindControl<ToggleButton>("MatchCaseBtn")!.IsChecked = false;
        Dispatcher.UIThread.RunJobs();

        bar.FindControl<ToggleButton>("ExpandBtn")!.IsChecked = true;
        bar.FindControl<TextBox>("ReplaceBox")!.Text = "BYE";
        Dispatcher.UIThread.RunJobs();
        var replaceAll = bar.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Replace All");
        replaceAll.Command?.Execute(null);
        replaceAll.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("10 PRINT \"BYE\"\n20 PRINT \"BYE AGAIN\"\n30 GOTO 10", editor.Document.Text);
        Assert.Equal("No results", count.Text);

        search.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.False(bar.IsVisible);
    }

    [AvaloniaFact]
    public void Replace_StepsThroughEveryMatch_EvenAfterTheDebouncedResearch_AndUpperCasesForBasic()
    {
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 900, Height = 400 };
        window.Show();
        var editor = window.FindControl<AvaloniaEdit.TextEditor>("Editor")!;
        var bar = window.FindControl<FindBarControl>("FindBar")!;
        editor.Document.Text = "10 A=FIRE\n20 B=FIRE\n30 C=FIRE";
        editor.CaretOffset = 0;
        Dispatcher.UIThread.RunJobs();

        window.KeyPressQwerty(PhysicalKey.F, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        bar.FindControl<ToggleButton>("ExpandBtn")!.IsChecked = true;
        bar.FindControl<TextBox>("SearchBox")!.Text = "fire";
        bar.FindControl<TextBox>("ReplaceBox")!.Text = "zapper";
        Dispatcher.UIThread.RunJobs();

        // Typing the term selects the first match.
        Assert.Equal("FIRE", editor.SelectedText);
        Assert.Equal(5, editor.SelectionStart);

        var replace = bar.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Replace");
        void ClickReplace()
        {
            replace.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            window.UpdateFindMatchesNow(); // the post-edit debounce must not advance the current match
            Dispatcher.UIThread.RunJobs();
        }

        ClickReplace();
        Assert.Equal("10 A=ZAPPER\n20 B=FIRE\n30 C=FIRE", editor.Document.Text);
        Assert.Equal("FIRE", editor.SelectedText);
        Assert.Equal(17, editor.SelectionStart); // line 20's match is current, not line 30's

        ClickReplace();
        Assert.Equal("10 A=ZAPPER\n20 B=ZAPPER\n30 C=FIRE", editor.Document.Text);

        ClickReplace();
        Assert.Equal("10 A=ZAPPER\n20 B=ZAPPER\n30 C=ZAPPER", editor.Document.Text);
    }


    #endregion
}
