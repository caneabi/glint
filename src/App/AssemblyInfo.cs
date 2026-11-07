using System.Runtime.InteropServices;

// Never load P/Invoke DLLs from the exe folder.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
