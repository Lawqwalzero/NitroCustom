using System.Management;

namespace NitroCustom.Core.Hardware;

public sealed class AcerWmiException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Raw access to root\WMI:AcerGamingFunction. Abstracted for tests.</summary>
public interface IAcerWmi
{
    /// <summary>Get* method with UInt32 input and UInt64 output.</summary>
    ulong Get(string method, uint input);

    /// <summary>Set* method with UInt64 input; returns the UInt32 status (0 = success).</summary>
    uint Set(string method, ulong input);

    /// <summary>Get* method with UInt32 input and byte-array output.</summary>
    (byte[] Output, byte Return) GetBytes(string method, uint input);

    /// <summary>Set* method with byte-array input; returns the UInt32 status.</summary>
    uint SetBytes(string method, byte[] input);
}

/// <summary>
/// The class is provided by the BIOS (ACPI WMI, wmiacpi.sys), not by Acer software.
/// Calls require administrator rights.
/// </summary>
public sealed class AcerWmi : IAcerWmi, IDisposable
{
    public const string Namespace = @"root\WMI";
    private readonly ManagementObject _gaming;
    private readonly Lock _lock = new();

    public AcerWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Namespace, "SELECT * FROM AcerGamingFunction");
            _gaming = searcher.Get().Cast<ManagementObject>().FirstOrDefault()
                ?? throw new AcerWmiException("AcerGamingFunction not found: unsupported laptop or BIOS.");
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            throw new AcerWmiException("Access denied to Acer WMI: run as administrator.", ex);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidClass)
        {
            throw new AcerWmiException("AcerGamingFunction not found: unsupported laptop or BIOS.", ex);
        }
    }

    public ulong Get(string method, uint input)
    {
        using var r = Invoke(method, input);
        return Convert.ToUInt64(r["gmOutput"]);
    }

    public uint Set(string method, ulong input)
    {
        using var r = Invoke(method, input);
        return Convert.ToUInt32(r["gmOutput"]);
    }

    public (byte[] Output, byte Return) GetBytes(string method, uint input)
    {
        using var r = Invoke(method, input);
        return ((byte[]?)r["gmOutput"] ?? [], Convert.ToByte(r["gmReturn"]));
    }

    public uint SetBytes(string method, byte[] input)
    {
        using var r = Invoke(method, input);
        return Convert.ToUInt32(r["gmOutput"]);
    }

    private ManagementBaseObject Invoke(string method, object input)
    {
        lock (_lock)
        {
            try
            {
                using var inParams = _gaming.GetMethodParameters(method);
                inParams["gmInput"] = input;
                return _gaming.InvokeMethod(method, inParams, null);
            }
            catch (ManagementException ex)
            {
                throw new AcerWmiException($"{method} failed: {ex.Message}", ex);
            }
        }
    }

    public void Dispose() => _gaming.Dispose();
}
