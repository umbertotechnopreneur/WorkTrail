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

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace WorkTrail.Application;

/// <summary>Provides product metadata and opens allowlisted public product links.</summary>
public sealed partial class WorkTrailApplication
{

    #region Methods and properties
    /// <inheritdoc />
    public Task<OperationResult<ProductInformation>> GetProductInformationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProductInformation(
            "WorkTrail",
            "MIT License",
            ProductRepositoryUrl,
            ProductAuthorUrl,
            _buildInformation.Load());
        return Task.FromResult(OperationResult<ProductInformation>.Success("product.loaded", "ProductInformationLoaded", info));
    }

    /// <inheritdoc />
    /// <param name="linkKey">The semantic key of an allowlisted public product link.</param>
    /// <param name="cancellationToken">Cancels the request before launching the external handler.</param>
    /// <exception cref="OperationCanceledException">The request was cancelled before launch.</exception>
    public async Task<OperationResult<bool>> OpenProductLinkAsync(string linkKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = linkKey switch
        {
            // Use the Store-associated family identity, including in unpackaged development runs.
            "store" => "ms-windows-store://pdp/?PFN=UmbertoGiacobbiDotBiz.WorkTrail_aa9ddh7dsmn36",
            "author" => ProductAuthorUrl,
            "repository" => ProductRepositoryUrl,
            "vibeware" => "https://github.com/umbertotechnopreneur/VibeWare",
            "vibeware-manifesto" => "https://umbertogiacobbi.biz/vibeware/manifesto/",
            "privacy" => ProductPrivacyUrl,
            "terms" => ProductTermsUrl,
            "issues" => ProductIssuesUrl,
            "openweather" => OpenWeatherUrl,
            "openweather-keys" => "https://home.openweathermap.org/api_keys",
            "openweather-guide" => "https://openweathermap.org/faq#how-to-get-an-API-key",
            "openweather-pricing" => "https://openweathermap.org/price",
            _ => null
        };
        if (target is null)
        {
            return OperationResult<bool>.Failure(
                "product.link.invalid",
                "ProductLinkInvalid",
                new ValidationIssue("linkKey", "unsupported", "ProductLinkInvalid"));
        }

        try
        {
            if (linkKey == "store")
            {
                // Store activation can reuse an existing process; use the Windows launch result rather than a process handle.
                if (!await Windows.System.Launcher.LaunchUriAsync(new Uri(target)))
                {
                    throw new InvalidOperationException("Windows did not open Microsoft Store.");
                }
            }
            else
            {
                _ = Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true })
                    ?? throw new InvalidOperationException("Windows did not open the product link.");
            }
            _logger.LogInformation("Product link opened. Link={Link}", linkKey);
            return OperationResult<bool>.Success("product.link.opened", "ProductLinkOpened", true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                NotSupportedException or
                System.ComponentModel.Win32Exception or
                System.Runtime.InteropServices.COMException or
                UnauthorizedAccessException)
        {
            _logger.LogWarning("Product link could not be opened. Link={Link} ExceptionType={ExceptionType}", linkKey, exception.GetType().Name);
            return OperationResult<bool>.Failure("product.link.unavailable", "ProductLinkUnavailable");
        }
    }

    #endregion

}
