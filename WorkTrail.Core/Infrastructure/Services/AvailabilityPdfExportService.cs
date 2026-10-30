// SPDX-License-Identifier: MIT
using WorkTrail.Application;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace WorkTrail.Services;

/// <summary>Owns the native destination picker and transactional PDF persistence on the caller's UI thread.</summary>
internal static class AvailabilityPdfExportService
{
    /// <summary>Creates a PDF from frozen data and lets the user choose its destination; cancellation is not success.</summary>
    internal static async Task<OperationResult<string?>> ExportAsync(long ownerWindowHandle, AvailabilityPdfRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (ownerWindowHandle == 0) throw new ArgumentException("An owner window is required.", nameof(ownerWindowHandle));
        // Render before opening the picker so an unsuccessful render cannot create an empty destination file.
        var bytes = await Task.Run(() => AvailabilityPdfRenderer.Render(request, AppContext.BaseDirectory, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"WorkTrail-{request.Snapshot.ReferenceLocalTime:yyyy-MM-dd}",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, new IntPtr(ownerWindowHandle));
        var file = await picker.PickSaveFileAsync().AsTask(cancellationToken);
        if (file is null) return OperationResult<string?>.Success("planner.pdf.cancelled", "OperationCancelled");
        // Transactional replacement keeps an existing destination intact when writing or cancellation fails.
        using var transaction = await file.OpenTransactedWriteAsync().AsTask(cancellationToken);
        transaction.Stream.Size = 0;
        using var writer = new DataWriter(transaction.Stream);
        writer.WriteBytes(bytes);
        await writer.StoreAsync().AsTask(cancellationToken);
        await writer.FlushAsync().AsTask(cancellationToken);
        writer.DetachStream();
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync();
        return OperationResult<string?>.Success("planner.pdf.saved", "Planner.PdfSaved", file.Path);
    }
}
