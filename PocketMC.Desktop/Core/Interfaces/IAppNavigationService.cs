using System;
using System.Windows.Controls;

namespace PocketMC.Desktop.Core.Interfaces
{
    public enum DetailRouteKind
    {
        NewInstance,
        ServerSettings,
        PluginBrowser,
        ServerConsole,
        TunnelCreationGuide,
        PlayitGuide
    }

    public enum DetailBackNavigation
    {
        Dashboard,
        Tunnel,
        PreviousDetail
    }

    public interface IAppNavigationService
    {
        bool NavigateToDashboard();
        bool NavigateToTunnel();
        bool NavigateToShellPage(Type pageType);
        bool NavigateToDetailPage(
            Page page,
            string breadcrumbLabel,
            DetailRouteKind routeKind,
            DetailBackNavigation backNavigation,
            bool clearDetailStack = false);
        bool NavigateBack();
    }
}
