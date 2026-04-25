using System.Net;
using System.Runtime.InteropServices;
using System.Linq;

namespace RagnarokAutomation.Core;

public sealed class SocketInspector
{
    private const int AfInet = 2;
    private const uint ErrorInsufficientBuffer = 122;

    private enum TcpTableClass
    {
        TcpTableOwnerPidAll = 5
    }

    private enum TcpState
    {
        Closed = 1,
        Listen = 2,
        SynSent = 3,
        SynRcvd = 4,
        Established = 5,
        FinWait1 = 6,
        FinWait2 = 7,
        CloseWait = 8,
        Closing = 9,
        LastAck = 10,
        TimeWait = 11,
        DeleteTcb = 12
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] LocalPort;
        public uint RemoteAddr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int outBufferLen,
        bool sort,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);

    public ProcessSnapshot GetSnapshot(int processId, string processName, bool processAlive)
    {
        int established = 0;
        int closing = 0;
        HashSet<int> ports = [];

        foreach ((IPAddress remoteIp, int remotePort, TcpState state) in GetRowsForPid(processId))
        {
            if (!remoteIp.Equals(IPAddress.None) && remotePort > 0)
            {
                ports.Add(remotePort);
            }
            if (state == TcpState.Established)
            {
                established++;
            }
            else if (state is TcpState.CloseWait or TcpState.Closing or TcpState.FinWait1 or TcpState.FinWait2 or TcpState.LastAck)
            {
                closing++;
            }
        }

        return new ProcessSnapshot
        {
            ProcessId = processId,
            ProcessName = processName,
            IsProcessAlive = processAlive,
            EstablishedConnections = established,
            ClosingConnections = closing,
            RemotePorts = ports.Count == 0 ? "-" : string.Join(", ", ports.OrderBy(p => p))
        };
    }

    private static IEnumerable<(IPAddress remoteIp, int remotePort, TcpState state)> GetRowsForPid(int processId)
    {
        int bufferSize = 0;
        uint initial = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableClass.TcpTableOwnerPidAll, 0);
        if (initial != ErrorInsufficientBuffer || bufferSize <= 0)
        {
            yield break;
        }

        IntPtr tablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            uint result = GetExtendedTcpTable(tablePtr, ref bufferSize, true, AfInet, TcpTableClass.TcpTableOwnerPidAll, 0);
            if (result != 0)
            {
                yield break;
            }

            int count = Marshal.ReadInt32(tablePtr);
            IntPtr rowPtr = IntPtr.Add(tablePtr, 4);
            int rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (int i = 0; i < count; i++)
            {
                MibTcpRowOwnerPid row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                rowPtr = IntPtr.Add(rowPtr, rowSize);

                if (row.OwningPid != (uint)processId || row.RemoteAddr == 0)
                {
                    continue;
                }

                yield return (new IPAddress(row.RemoteAddr), PortFromBytes(row.RemotePort), (TcpState)row.State);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tablePtr);
        }
    }

    private static int PortFromBytes(byte[] bytes)
    {
        if (bytes.Length < 2)
        {
            return 0;
        }

        return (bytes[0] << 8) + bytes[1];
    }
}
