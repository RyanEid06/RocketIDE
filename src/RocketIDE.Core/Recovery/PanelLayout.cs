namespace RocketIDE.Core.Recovery;

public sealed record PanelLayout(
    bool ExplorerVisible = true,
    bool BottomPanelVisible = true,
    double ExplorerWidth = 250,
    double BottomPanelHeight = 190,
    int BottomTabIndex = 0);
