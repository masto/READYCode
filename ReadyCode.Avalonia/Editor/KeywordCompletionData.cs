// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using ReadyCode.Editor;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// AvaloniaEdit <see cref="ICompletionData"/> adapter over a shared
/// <see cref="KeywordCompletionItem"/>, for both the BASIC and assembly providers. The WPF app
/// has the same class over AvalonEdit's identically shaped interface.
/// </summary>
public sealed class KeywordCompletionData : ICompletionData
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="KeywordCompletionData"/> class.
    /// </summary>
    /// <param name="item">The editor-independent completion entry to present.</param>
    public KeywordCompletionData(KeywordCompletionItem item)
    {
        Item = item;
    }

    #endregion

    #region Public Properties

    /// <summary>Gets the underlying editor-independent completion entry.</summary>
    public KeywordCompletionItem Item { get; }

    /// <summary>Gets the icon shown next to the entry. Always null; no icons are used.</summary>
    public IImage? Image => null;

    /// <summary>Gets the keyword text the popup filters on.</summary>
    public string Text => Item.Text;

    /// <summary>Gets the content displayed in the completion list (same as <see cref="Text"/>).</summary>
    public object Content => Text;

    /// <summary>Gets the description shown for the selected entry.</summary>
    public object Description => Item.Description;

    /// <summary>Gets the sort priority. Always zero; the providers hand us items already sorted.</summary>
    public double Priority => 0;

    /// <summary>Gets the snippet text with the '|' caret marker removed.</summary>
    public string Snippet => Item.InsertText;

    #endregion

    #region Public Methods

    /// <summary>
    /// Replaces the typed prefix with this entry's snippet and puts the caret at its marker.
    /// </summary>
    /// <param name="textArea">The text area to insert into.</param>
    /// <param name="completionSegment">The prefix being replaced.</param>
    /// <param name="insertionRequestEventArgs">The event that requested the insertion.</param>
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Item.InsertText);
        textArea.Caret.Offset = completionSegment.Offset + Item.CaretOffset;
    }

    #endregion
}
