global using System.IO;
global using System.Diagnostics;
global using System.Net.Http;

// P/Invoke targets (wintrust.dll) resolve from System32, never from the folder
// the exe was launched from.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
