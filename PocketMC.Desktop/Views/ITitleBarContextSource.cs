using System;
using System.Windows.Media;

namespace PocketMC.Desktop.Views;

public interface ITitleBarContextSource
{
    string? TitleBarContextTitle { get; }
    string? TitleBarContextStatusText { get; }
    Brush? TitleBarContextStatusBrush { get; }
    event Action? TitleBarContextChanged;
}
