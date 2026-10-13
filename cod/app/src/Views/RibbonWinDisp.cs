using System.Windows;

namespace ShouTech.App.Views;

/// <summary>
/// Phase 7 - key-based dispatcher that opens dedicated modern ribbon windows
/// for actions that previously fell through to the generic module workspace.
/// Keeps MWin / MainShVM free of per-window knowledge.
/// </summary>
internal static class RibbonWinDisp
{
    /// <summary>
    /// Returns true when the action key was handled by a dedicated window.
    /// </summary>
    public static bool TryHandle(string? actionKey, Window? owner)
    {
        switch (actionKey ?? string.Empty)
        {
            case "Tools.Calculator":
                CalcWin.Open(owner);
                return true;

            case "Tools.Keyboard":
                KbdWin.Open(owner);
                return true;

            case "Help.About":
            case "حول البرنامج":
                AbtWin.Open(owner);
                return true;

            case "Help.Instructions":
                HlpWin.Open(owner);
                return true;

            case "Help.Suggestions":
                FdbWin.Open(owner);
                return true;

            case "View.InfoWindow":
                InfoWin.Open(owner);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Shared single-instance show helper used by all Phase 7 windows.
    /// </summary>
    internal static void ShowSingleton(Window? owner, Window window)
    {
        foreach (Window open in System.Windows.Application.Current.Windows)
        {
            if (ReferenceEquals(open, window) || !open.IsVisible)
                continue;

            if (open.GetType() == window.GetType())
            {
                if (open.WindowState == WindowState.Minimized)
                    open.WindowState = WindowState.Normal;

                open.Activate();
                open.Focus();

                window.Close();
                return;
            }
        }

        if (owner != null && owner.IsVisible)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.Show();
        window.Activate();
    }
}
