using System;
using System.Windows.Media;

namespace PocketMC.Desktop.Core.Interfaces;

public interface ITitleBarContextSource
{
    string? TitleBarContextTitle { get; }
    string? TitleBarContextStatusText { get; }
    Brush? TitleBarContextStatusBrush { get; }
    event Action? TitleBarContextChanged;
}
