using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace HollowKnightTAS.Runtime.Ipc
{
    internal sealed class NativeNamedPipeServer : IDisposable
    {
        private const uint PipeAccessInbound = 0x00000001;
        private const uint PipeAccessOutbound = 0x00000002;
        private const uint PipeAccessDuplex = 0x00000003;
        private const uint FileFlagFirstPipeInstance = 0x00080000;
        private const uint FileFlagOverlapped = 0x40000000;
        private const uint PipeTypeByte = 0x00000000;
        private const uint PipeReadModeByte = 0x00000000;
        private const uint PipeWait = 0x00000000;
        private const uint PipeRejectRemoteClients = 0x00000008;
        private const int ErrorBrokenPipe = 109;
        private const int ErrorNoData = 232;
        private const int ErrorOperationAborted = 995;
        private const int ErrorIoPending = 997;
        private const int ErrorPipeConnected = 535;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 258;
        private const uint WaitFailed = 0xFFFFFFFF;
        private const uint Infinite = 0xFFFFFFFF;
        private const int PipeFullControl = 0x001F019F;

        private readonly SafePipeHandle handle;
        private readonly NativePipeStream readStream;
        private readonly NativePipeStream writeStream;
        private readonly PipeDirection direction;
        private int connected;
        private int disposed;

        public NativeNamedPipeServer(
            string pipeName,
            PipeDirection direction)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new ArgumentException(
                    "Pipe name is required.",
                    nameof(pipeName));
            }

            this.direction = direction;
            var sid = WindowsCurrentUserSid.Read();
            var acl = new RawAcl(GenericAcl.AclRevision, 1);
            acl.InsertAce(
                0,
                new CommonAce(
                    AceFlags.None,
                    AceQualifier.AccessAllowed,
                    PipeFullControl,
                    sid,
                    false,
                    null));
            var descriptor = new RawSecurityDescriptor(
                ControlFlags.DiscretionaryAclPresent
                | ControlFlags.SelfRelative,
                null,
                null,
                null,
                acl);
            var descriptorBytes =
                new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(descriptorBytes, 0);
            var pinnedDescriptor =
                GCHandle.Alloc(
                    descriptorBytes,
                    GCHandleType.Pinned);
            try
            {
                var attributes = new SecurityAttributes
                {
                    Length = Marshal.SizeOf(
                        typeof(SecurityAttributes)),
                    SecurityDescriptor =
                        pinnedDescriptor.AddrOfPinnedObject(),
                    InheritHandle = false
                };
                var rawHandle = CreateNamedPipe(
                    @"\\.\pipe\" + pipeName,
                    AccessMode(direction)
                    | FileFlagFirstPipeInstance
                    | FileFlagOverlapped,
                    PipeTypeByte
                    | PipeReadModeByte
                    | PipeWait
                    | PipeRejectRemoteClients,
                    1,
                    64 * 1024,
                    64 * 1024,
                    0,
                    ref attributes);
                if (rawHandle == new IntPtr(-1))
                {
                    throw CreateWin32Exception(
                        Marshal.GetLastWin32Error(),
                        "CreateNamedPipe");
                }

                handle = new SafePipeHandle(
                    rawHandle,
                    ownsHandle: true);
            }
            finally
            {
                pinnedDescriptor.Free();
            }

            readStream = new NativePipeStream(
                this,
                canRead: direction != PipeDirection.Out,
                canWrite: false);
            writeStream = new NativePipeStream(
                this,
                canRead: false,
                canWrite: direction != PipeDirection.In);
        }

        public Stream ReadStream
        {
            get
            {
                ThrowIfDisposed();
                if (direction == PipeDirection.Out)
                {
                    throw new NotSupportedException(
                        "This pipe is write-only.");
                }

                return readStream;
            }
        }

        public bool IsConnected =>
            Volatile.Read(ref disposed) == 0
            && Volatile.Read(ref connected) != 0;

        public Stream WriteStream
        {
            get
            {
                ThrowIfDisposed();
                if (direction == PipeDirection.In)
                {
                    throw new NotSupportedException(
                        "This pipe is read-only.");
                }

                return writeStream;
            }
        }

        public void WaitForConnection()
        {
            ThrowIfDisposed();
            if (Volatile.Read(ref connected) != 0)
            {
                return;
            }

            using (var operation = new NativeOperation())
            {
                var completed = ConnectNamedPipe(
                    handle,
                    operation.Pointer);
                if (!completed)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorPipeConnected)
                    {
                        Interlocked.Exchange(ref connected, 1);
                        return;
                    }

                    if (error != ErrorIoPending)
                    {
                        throw CreateWin32Exception(
                            error,
                            "ConnectNamedPipe");
                    }

                    WaitForOperation(
                        operation,
                        CancellationToken.None);
                    if (!GetOverlappedResult(
                            handle,
                            operation.Pointer,
                            out _,
                            false))
                    {
                        error = Marshal.GetLastWin32Error();
                        if (error == ErrorOperationAborted
                            && Volatile.Read(ref disposed) != 0)
                        {
                            throw new ObjectDisposedException(
                                nameof(NativeNamedPipeServer));
                        }

                        throw CreateWin32Exception(
                            error,
                            "ConnectNamedPipe");
                    }
                }
            }

            ThrowIfDisposed();
            Interlocked.Exchange(ref connected, 1);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            CancelIoEx(handle, IntPtr.Zero);
            Interlocked.Exchange(ref connected, 0);
            handle.Dispose();
        }

        private static uint AccessMode(
            PipeDirection direction)
        {
            switch (direction)
            {
                case PipeDirection.In:
                    return PipeAccessInbound;
                case PipeDirection.Out:
                    return PipeAccessOutbound;
                case PipeDirection.InOut:
                    return PipeAccessDuplex;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(direction));
            }
        }

        private int Read(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateBuffer(buffer, offset, count);
            if (count == 0)
            {
                return 0;
            }

            return ExecuteIo(
                buffer,
                offset,
                count,
                write: false,
                cancellationToken);
        }

        private void Write(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateBuffer(buffer, offset, count);
            var written = 0;
            while (written < count)
            {
                var current = ExecuteIo(
                    buffer,
                    offset + written,
                    count - written,
                    write: true,
                    cancellationToken);
                if (current <= 0)
                {
                    throw new EndOfStreamException(
                        "Native named pipe ended during write.");
                }

                written += current;
            }
        }

        private int ExecuteIo(
            byte[] buffer,
            int offset,
            int count,
            bool write,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pinnedBuffer = GCHandle.Alloc(
                buffer,
                GCHandleType.Pinned);
            try
            {
                using (var operation = new NativeOperation())
                {
                    var bufferPointer = IntPtr.Add(
                        pinnedBuffer.AddrOfPinnedObject(),
                        offset);
                    uint ignored;
                    var completed = write
                        ? WriteFile(
                            handle,
                            bufferPointer,
                            checked((uint)count),
                            out ignored,
                            operation.Pointer)
                        : ReadFile(
                            handle,
                            bufferPointer,
                            checked((uint)count),
                            out ignored,
                            operation.Pointer);
                    if (!completed)
                    {
                        var error = Marshal.GetLastWin32Error();
                        if (error != ErrorIoPending)
                        {
                            return HandleIoFailure(
                                error,
                                write,
                                cancellationToken);
                        }

                        WaitForOperation(
                            operation,
                            cancellationToken);
                    }

                    if (!GetOverlappedResult(
                            handle,
                            operation.Pointer,
                            out var transferred,
                            false))
                    {
                        return HandleIoFailure(
                            Marshal.GetLastWin32Error(),
                            write,
                            cancellationToken);
                    }

                    return checked((int)transferred);
                }
            }
            finally
            {
                pinnedBuffer.Free();
            }
        }

        private void WaitForOperation(
            NativeOperation operation,
            CancellationToken cancellationToken)
        {
            var cancelIssued = false;
            while (true)
            {
                if (!cancelIssued
                    && (cancellationToken.IsCancellationRequested
                        || Volatile.Read(ref disposed) != 0))
                {
                    cancelIssued = true;
                    CancelIoEx(handle, operation.Pointer);
                }

                var wait = WaitForSingleObject(
                    operation.EventHandle,
                    cancelIssued ? Infinite : 25);
                if (wait == WaitObject0)
                {
                    return;
                }

                if (wait == WaitTimeout)
                {
                    continue;
                }

                if (wait == WaitFailed)
                {
                    var error = Marshal.GetLastWin32Error();
                    CancelIoEx(handle, operation.Pointer);
                    WaitForSingleObject(
                        operation.EventHandle,
                        Infinite);
                    throw CreateWin32Exception(
                        error,
                        "WaitForSingleObject");
                }

                CancelIoEx(handle, operation.Pointer);
                WaitForSingleObject(
                    operation.EventHandle,
                    Infinite);
                throw new IOException(
                    "Unexpected native pipe wait result: "
                    + wait + ".");
            }
        }

        private int HandleIoFailure(
            int error,
            bool write,
            CancellationToken cancellationToken)
        {
            if (error == ErrorOperationAborted
                && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    cancellationToken);
            }

            if (!write
                && (error == ErrorBrokenPipe
                    || error == ErrorNoData))
            {
                return 0;
            }

            if (error == ErrorOperationAborted
                && Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(NativeNamedPipeServer));
            }

            throw CreateWin32Exception(
                error,
                write ? "WriteFile" : "ReadFile");
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(
                    nameof(NativeNamedPipeServer));
            }
        }

        private static void ValidateBuffer(
            byte[] buffer,
            int offset,
            int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0
                || offset > buffer.Length - count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        private static IOException CreateWin32Exception(
            int error,
            string operation)
        {
            return new IOException(
                operation + " failed with Win32 error " + error + ".",
                new Win32Exception(error));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;

            [MarshalAs(UnmanagedType.Bool)]
            public bool InheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeOverlappedData
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr EventHandle;
        }

        private sealed class NativeOperation : IDisposable
        {
            public NativeOperation()
            {
                EventHandle = CreateEvent(
                    IntPtr.Zero,
                    true,
                    false,
                    null);
                if (EventHandle == IntPtr.Zero)
                {
                    throw CreateWin32Exception(
                        Marshal.GetLastWin32Error(),
                        "CreateEvent");
                }

                try
                {
                    Pointer = Marshal.AllocHGlobal(
                        Marshal.SizeOf(
                            typeof(NativeOverlappedData)));
                    Marshal.StructureToPtr(
                        new NativeOverlappedData
                        {
                            EventHandle = EventHandle
                        },
                        Pointer,
                        false);
                }
                catch
                {
                    CloseHandle(EventHandle);
                    throw;
                }
            }

            public IntPtr EventHandle { get; }
            public IntPtr Pointer { get; }

            public void Dispose()
            {
                Marshal.FreeHGlobal(Pointer);
                CloseHandle(EventHandle);
            }
        }

        private sealed class NativePipeStream : Stream
        {
            private readonly NativeNamedPipeServer owner;
            private readonly bool canRead;
            private readonly bool canWrite;

            public NativePipeStream(
                NativeNamedPipeServer owner,
                bool canRead,
                bool canWrite)
            {
                this.owner = owner;
                this.canRead = canRead;
                this.canWrite = canWrite;
            }

            public override bool CanRead => canRead;
            public override bool CanSeek => false;
            public override bool CanWrite => canWrite;

            public override long Length =>
                throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
                owner.ThrowIfDisposed();
            }

            public override Task FlushAsync(
                CancellationToken cancellationToken)
            {
                owner.ThrowIfDisposed();
                if (cancellationToken.IsCancellationRequested)
                {
                    return Task.FromCanceled(cancellationToken);
                }

                return Task.CompletedTask;
            }

            public override int Read(
                byte[] buffer,
                int offset,
                int count)
            {
                if (!canRead)
                {
                    throw new NotSupportedException(
                        "This stream is write-only.");
                }

                return owner.Read(
                    buffer,
                    offset,
                    count,
                    CancellationToken.None);
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                if (!canRead)
                {
                    throw new NotSupportedException(
                        "This stream is write-only.");
                }

                ValidateBuffer(buffer, offset, count);
                return Task.Run(
                    () => owner.Read(
                        buffer,
                        offset,
                        count,
                        cancellationToken));
            }

            public override void Write(
                byte[] buffer,
                int offset,
                int count)
            {
                if (!canWrite)
                {
                    throw new NotSupportedException(
                        "This stream is read-only.");
                }

                owner.Write(
                    buffer,
                    offset,
                    count,
                    CancellationToken.None);
            }

            public override Task WriteAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                if (!canWrite)
                {
                    throw new NotSupportedException(
                        "This stream is read-only.");
                }

                ValidateBuffer(buffer, offset, count);
                return Task.Run(
                    () => owner.Write(
                        buffer,
                        offset,
                        count,
                        cancellationToken));
            }

            public override long Seek(
                long offset,
                SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }
        }

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern IntPtr CreateNamedPipe(
            string name,
            uint openMode,
            uint pipeMode,
            uint maximumInstances,
            uint outputBufferSize,
            uint inputBufferSize,
            uint defaultTimeout,
            ref SecurityAttributes securityAttributes);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ConnectNamedPipe(
            SafePipeHandle pipe,
            IntPtr overlapped);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadFile(
            SafePipeHandle file,
            IntPtr buffer,
            uint bytesToRead,
            out uint bytesRead,
            IntPtr overlapped);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteFile(
            SafePipeHandle file,
            IntPtr buffer,
            uint bytesToWrite,
            out uint bytesWritten,
            IntPtr overlapped);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOverlappedResult(
            SafePipeHandle file,
            IntPtr overlapped,
            out uint bytesTransferred,
            [MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(
            SafePipeHandle handle,
            IntPtr overlapped);

        [DllImport(
            "kernel32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern IntPtr CreateEvent(
            IntPtr eventAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool manualReset,
            [MarshalAs(UnmanagedType.Bool)] bool initialState,
            string? name);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        private static extern uint WaitForSingleObject(
            IntPtr handle,
            uint milliseconds);

        [DllImport(
            "kernel32.dll",
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
