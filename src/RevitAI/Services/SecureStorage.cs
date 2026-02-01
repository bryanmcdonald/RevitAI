// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Security.Cryptography;
using System.Text;

namespace RevitAI.Services;

/// <summary>
/// Provides secure storage for sensitive data using Windows DPAPI.
/// Data is encrypted per-user; other Windows users cannot decrypt.
/// </summary>
public static class SecureStorage
{
    /// <summary>
    /// Encrypts a string using Windows DPAPI (per-user scope).
    /// </summary>
    /// <param name="plainText">The text to encrypt.</param>
    /// <returns>Base64-encoded encrypted data, or null if input is null/empty.</returns>
    public static string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var encryptedBytes = ProtectedData.Protect(
                plainBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch (CryptographicException)
        {
            // Encryption failed - return null
            return null;
        }
    }

    /// <summary>
    /// Decrypts a string that was encrypted with <see cref="Encrypt"/>.
    /// </summary>
    /// <param name="encryptedText">Base64-encoded encrypted data.</param>
    /// <returns>The decrypted plain text, or null if decryption fails.</returns>
    public static string? Decrypt(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText))
            return null;

        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedText);
            var plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // Decryption failed (different user, corrupted data, etc.)
            return null;
        }
        catch (FormatException)
        {
            // Invalid Base64 string
            return null;
        }
    }
}
