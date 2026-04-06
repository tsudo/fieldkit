using System.Runtime.InteropServices;

namespace FieldKit.Tasks;

public class RecycleBinTask : MaintenanceTask
{
    public RecycleBinTask() : base(
        "Empty Recycle Bin",
        "Permanently deletes files you've already sent to the Recycle Bin") { }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    public override Task GatherContextAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            if (SHQueryRecycleBin(null, ref info) == 0)
            {
                ContextInfo = info.i64NumItems == 0
                    ? "Empty"
                    : $"{info.i64NumItems} items, {FormatBytes(info.i64Size)}";
            }
        }
        catch { }
    }, ct);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;
    private const int S_OK = 0;
    private const int E_UNEXPECTED = -2147418113;       // 0x8000FFFF — bin already empty on some systems
    private const int E_NO_MORE_FILES = unchecked((int)0x80070012); // alternate "empty" error

    public override async Task<TaskResult> ExecuteAsync(bool dryRun, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            if (dryRun)
            {
                Log("[DryRun] Would empty Recycle Bin", "INFO");
                return TaskResult.Ok("Dry run");
            }

            try
            {
                Log("Emptying Recycle Bin...", "INFO");

                // SHEmptyRecycleBin can block if the bin is large or a COM dialog
                // gets stuck. Run on a dedicated STA thread with a timeout.
                int hr = S_OK;
                var thread = new Thread(() =>
                {
                    hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                        SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();

                if (!thread.Join(TimeSpan.FromSeconds(60)))
                {
                    Log("Recycle Bin operation timed out after 60 s — moving on.", "WARN");
                    return TaskResult.Warn("Timed out — may still be emptying");
                }

                ct.ThrowIfCancellationRequested();

                if (hr == S_OK)
                {
                    Log("Emptied Recycle Bin.", "OK");
                    return TaskResult.Ok();
                }
                else if (hr == E_UNEXPECTED || hr == E_NO_MORE_FILES)
                {
                    Log("Recycle Bin was already empty.", "OK");
                    return TaskResult.Ok("Already empty");
                }
                else
                {
                    Log($"SHEmptyRecycleBin returned 0x{hr:X8}", "WARN");
                    return TaskResult.Warn($"HRESULT 0x{hr:X8}");
                }
            }
            catch (Exception ex)
            {
                Log($"Could not empty Recycle Bin: {ex.Message}", "WARN");
                return TaskResult.Warn(ex.Message);
            }
        }, ct);
    }
}
