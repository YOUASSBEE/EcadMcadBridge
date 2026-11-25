using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace EcadMcadBridge.SolidWorks.Interop;

/// <summary>
/// Connects to an already running SOLIDWORKS instance through the running object table.
/// It never starts or terminates SOLIDWORKS.
/// </summary>
public static class SolidWorksConnection
{
    private const string ProgId = "SldWorks.Application";

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(
        ref Guid rclsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public static bool TryConnect(out ISldWorks? solidWorks, out string message)
    {
        solidWorks = null;
        var type = Type.GetTypeFromProgID(ProgId);
        if (type is null)
        {
            message = "SOLIDWORKS ProgID is not registered.";
            return false;
        }

        var clsid = type.GUID;
        try
        {
            GetActiveObject(ref clsid, IntPtr.Zero, out var instance);
            solidWorks = instance as ISldWorks;
            if (solidWorks is null)
            {
                message = "The running object is not an ISldWorks instance.";
                return false;
            }

            message = string.Empty;
            return true;
        }
        catch (COMException ex)
        {
            message = "No running SOLIDWORKS instance was found (HRESULT 0x" +
                ex.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + ").";
            return false;
        }
        catch (Exception ex)
        {
            message = "SOLIDWORKS connection failed: " + ex.GetType().Name + ".";
            return false;
        }
    }
}
