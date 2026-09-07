// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using ReadyCode.Diagnostics;

namespace ReadyCode.Avalonia.Editor;

/// <summary>
/// Draws a wavy underline beneath each diagnostic's span, VS Code style.
/// </summary>
public sealed class ErrorSquiggleRenderer : IBackgroundRenderer
{
    private const double SquiggleAmplitude = 1.3;
    private const double SquiggleWavelength = 3.5;

    private readonly TextEditor _editor;
    private IPen _pen;
    private IReadOnlyList<EditorDiagnostic> _diagnostics = Array.Empty<EditorDiagnostic>();

    public ErrorSquiggleRenderer(TextEditor editor)
    {
        _editor = editor;
        _pen = MakePen(Colors.Red);
    }

    public KnownLayer Layer => KnownLayer.Background;

    public void SetColor(Color color) => _pen = MakePen(color);

    public void SetDiagnostics(IReadOnlyList<EditorDiagnostic> diagnostics) => _diagnostics = diagnostics;

    public void Clear() => _diagnostics = Array.Empty<EditorDiagnostic>();

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_editor.Document == null || _diagnostics.Count == 0) return;

        textView.EnsureVisualLines();
        foreach (var vl in textView.VisualLines)
        {
            int lineStart = vl.FirstDocumentLine.Offset;
            int lineEnd   = vl.LastDocumentLine.EndOffset;

            foreach (var diag in _diagnostics)
            {
                if (diag.Offset + diag.Length <= lineStart || diag.Offset >= lineEnd) continue;

                int start = Math.Max(diag.Offset, lineStart);
                int end   = Math.Min(diag.Offset + diag.Length, lineEnd);
                if (end <= start) continue;

                double x1 = GetVisualX(vl, textView, start - lineStart, isAtEndOfLine: false);
                double x2 = GetVisualX(vl, textView, end - lineStart, isAtEndOfLine: end == lineEnd);
                double y  = vl.VisualTop - textView.ScrollOffset.Y + vl.Height - 2;
                DrawSquiggle(drawingContext, x1, x2, y);
            }
        }
    }

    private static double GetVisualX(VisualLine vl, TextView textView, int relativeOffset, bool isAtEndOfLine)
    {
        int visualColumn = vl.GetVisualColumn(relativeOffset);
        var textLine = vl.GetTextLine(visualColumn, isAtEndOfLine);
        return vl.GetTextLineVisualXPosition(textLine, visualColumn) - textView.ScrollOffset.X;
    }

    // Each "hump" is a quadratic bezier back to the baseline - a smooth wave reads better than a
    // sharp zigzag at small font sizes.
    private void DrawSquiggle(DrawingContext drawingContext, double x1, double x2, double y)
    {
        if (x2 <= x1) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x1, y), false);
            bool up = false;
            double x = x1;
            while (x < x2)
            {
                double nextX = Math.Min(x + SquiggleWavelength, x2);
                double midX  = (x + nextX) / 2;
                double waveY = up ? y - SquiggleAmplitude : y + SquiggleAmplitude;
                ctx.QuadraticBezierTo(new Point(midX, waveY), new Point(nextX, y));
                up = !up;
                x = nextX;
            }
            ctx.EndFigure(false);
        }

        drawingContext.DrawGeometry(null, _pen, geometry);
    }

    private static IPen MakePen(Color color) =>
        new Pen(new SolidColorBrush(color), 1.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
}
