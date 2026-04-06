using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;

namespace _4RTools.Utils
{
    internal static class TcpEndpointInspector
    {
        private const int AF_INET = 2;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;

        private enum TCP_TABLE_CLASS
        {
            TCP_TABLE_OWNER_PID_ALL = 5
        }

        internal enum MibTcpState
        {
            CLOSED = 1,
            LISTEN = 2,
            SYN_SENT = 3,
            SYN_RCVD = 4,
            ESTAB = 5,
            FIN_WAIT1 = 6,
            FIN_WAIT2 = 7,
            CLOSE_WAIT = 8,
            CLOSING = 9,
            LAST_ACK = 10,
            TIME_WAIT = 11,
            DELETE_TCB = 12
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;
            public uint remoteAddr;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;
            public uint owningPid;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            TCP_TABLE_CLASS tblClass,
            uint reserved);

        internal sealed class TcpRow
        {
            public int Pid { get; set; }
            public IPAddress RemoteIP { get; set; }
            public int RemotePort { get; set; }
            public MibTcpState State { get; set; }
        }

        internal static List<TcpRow> GetRowsForPid(int pid)
        {
            List<TcpRow> rows = new List<TcpRow>();
            int buffSize = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            if (result != ERROR_INSUFFICIENT_BUFFER || buffSize <= 0)
            {
                return rows;
            }

            IntPtr tablePtr = Marshal.AllocHGlobal(buffSize);
            try
            {
                result = GetExtendedTcpTable(tablePtr, ref buffSize, true, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
                if (result != 0)
                {
                    return rows;
                }

                int entryCount = Marshal.ReadInt32(tablePtr);
                IntPtr rowPtr = IntPtr.Add(tablePtr, 4);
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));

                for (int i = 0; i < entryCount; i++)
                {
                    MIB_TCPROW_OWNER_PID row = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_PID));
                    rowPtr = IntPtr.Add(rowPtr, rowSize);

                    if (row.owningPid != (uint)pid || row.remoteAddr == 0)
                    {
                        continue;
                    }

                    rows.Add(new TcpRow
                    {
                        Pid = pid,
                        RemoteIP = new IPAddress(row.remoteAddr),
                        RemotePort = PortFromBytes(row.remotePort),
                        State = (MibTcpState)row.state
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tablePtr);
            }

            return rows;
        }

        private static int PortFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2)
            {
                return 0;
            }
            return (bytes[0] << 8) + bytes[1];
        }
    }
}
