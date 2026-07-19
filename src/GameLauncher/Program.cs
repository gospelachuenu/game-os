using GameLauncher;

Console.WriteLine("GameLauncher module starting...");

var storeModeGate = new StoreModeGate();
storeModeGate.StoreModeExited += () =>
    Console.WriteLine("[GameLauncher] Store mode exited; Focus Guardian resumed.");

Console.WriteLine("GameLauncher module ready. This process is a placeholder entry point;");
Console.WriteLine("URI launching, store-mode gating, and WMI process matching are library");
Console.WriteLine("logic consumed by ConsoleSupervisor, not run standalone.");
