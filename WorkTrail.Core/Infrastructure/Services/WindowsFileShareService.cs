// SPDX-License-Identifier: MIT
/* VBWR B
 *
 * Project: WorkTrail
 * Repository: https://github.com/umbertotechnopreneur/WorkTrail
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * Modified with AI: OpenAI Codex; added this header on 2026-10-10.
 * Human guidance: Umberto Giacobbi; requested VibeWare branding.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 *
 * VBWR E */


using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace WorkTrail.Services;

/// <summary>Opens the Windows Share UI for one validated local file.</summary>
public sealed class WindowsFileShareService
{
    private static readonly Guid DataTransferManagerIid = new(
        0xa5caee9b,
        0x8708,
        0x49d1,
        0x8d,
        0x36,
        0x67,
        0xd2,
        0x5a,
        0x8d,
        0xa0,
        0x0c);

    /// <summary>Registers one existing file as shareable content and opens the Windows Share UI.</summary>
    public string Share(string filePath, IntPtr windowHandle, string title, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("The shared file path must be absolute.", nameof(filePath));
        }

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The shared file no longer exists.", fullPath);
        }

        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid owner window handle is required.", nameof(windowHandle));
        }

        var storageFile = LoadStorageFile(fullPath);
        var (interop, dataTransferManager) = GetDataTransferManager(windowHandle);
        TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            try
            {
                args.Request.Data.Properties.Title = title;
                args.Request.Data.Properties.Description = description;
                args.Request.Data.RequestedOperation = DataPackageOperation.Copy;
                args.Request.Data.SetStorageItems([storageFile]);
            }
            catch
            {
                // The synchronous Share contract cannot propagate errors after opening; keep its fallback privacy-safe.
                args.Request.FailWithDisplayText("WorkTrail could not prepare the selected file.");
            }
            finally
            {
                sender.DataRequested -= handler;
            }
        };

        dataTransferManager.DataRequested += handler;
        try
        {
            // WinUI desktop apps must use per-HWND interop; current-view sharing is unsupported here.
            interop.ShowShareUIForWindow(windowHandle);
        }
        catch (COMException exception) when (IsUnsupported(exception))
        {
            dataTransferManager.DataRequested -= handler;
            throw CreateUnsupportedException(exception);
        }
        catch
        {
            dataTransferManager.DataRequested -= handler;
            throw;
        }

        return fullPath;
    }

    private static StorageFile LoadStorageFile(string filePath)
    {
        try
        {
            return StorageFile.GetFileFromPathAsync(filePath).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is COMException or UnauthorizedAccessException or FileNotFoundException)
        {
            throw new IOException("Windows could not prepare the selected file for sharing.", exception);
        }
    }

    private static (IDataTransferManagerInterop Interop, DataTransferManager Manager) GetDataTransferManager(IntPtr windowHandle)
    {
        try
        {
            IDataTransferManagerInterop interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var abi = interop.GetForWindow(windowHandle, DataTransferManagerIid);
            if (abi == IntPtr.Zero)
            {
                throw CreateUnsupportedException();
            }

            try
            {
                var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(abi)
                    ?? throw CreateUnsupportedException();
                return (interop, manager);
            }
            finally
            {
                Marshal.Release(abi);
            }
        }
        catch (PlatformNotSupportedException)
        {
            throw;
        }
        catch (COMException exception) when (IsUnsupported(exception))
        {
            throw CreateUnsupportedException(exception);
        }
        catch (InvalidCastException exception)
        {
            throw CreateUnsupportedException(exception);
        }
    }

    private static bool IsUnsupported(COMException exception) =>
        exception.HResult is unchecked((int)0x80004001) or unchecked((int)0x80004002) or unchecked((int)0x80070032);

    private static PlatformNotSupportedException CreateUnsupportedException(Exception? innerException = null) =>
        new("Windows Share UI is not supported by this Windows installation or app deployment.", innerException);

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);

        void ShowShareUIForWindow(IntPtr appWindow);
    }
}
