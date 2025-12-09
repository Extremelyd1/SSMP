using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using SSMP.Collection;
using UnityEngine;
using Logger = SSMP.Logging.Logger;

namespace SSMP.Util;

/// <summary>
/// Utility class for authentication related methods. 
/// </summary>
internal static class AuthUtil
{
    /// <summary>
    /// The length of the authentication key.
    /// </summary>
    public const int AuthKeyLength = 56;

    /// <summary>
    /// Lookup for authentication key characters to their byte value.
    /// </summary>
    private static readonly BiLookup<char, byte> AuthKeyLookup;

    /// <summary>
    /// Static constructor that initializes the bi-directional lookup.
    /// </summary>
    static AuthUtil()
    {
        // A string containing all possible characters for an authentication key
        const string authKeyCharacter =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

        AuthKeyLookup = new BiLookup<char, byte>();

        for (byte i = 0; i < authKeyCharacter.Length; i++)
        {
            AuthKeyLookup.Add(authKeyCharacter[i], i);
        }
    }

    /// <summary>
    /// Checks whether a given authentication key is valid or not.
    /// </summary>
    /// <param name="authKey">The authentication key in string form to check.</param>
    /// <returns>True if the given authentication key is valid, false otherwise.</returns>
    public static bool IsValidAuthKey(string? authKey)
    {
        if (authKey == null)
        {
            return false;
        }

        if (authKey.Length != AuthKeyLength)
        {
            return false;
        }

        foreach (var authKeyChar in authKey.ToCharArray())
        {
            if (!AuthKeyLookup.ContainsFirst(authKeyChar))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Generates a consistent authentication key based on the machine's MAC address (or fallback).
    /// </summary>
    /// <returns>The generated consistent authentication key.</returns>
    public static string GenerateAuthKey()
    {
        var identifier = GetPersistentIdentifier();
        var authKey = "";

        using var sha256 = SHA256.Create();

        // We need 56 characters. SHA256 gives 32 bytes.
        // We will hash the identifier + index counter to generate enough deterministic bytes.

        var bytesNeeded = AuthKeyLength;
        var currentBytes = new List<byte>();
        var counter = 0;

        while (currentBytes.Count < bytesNeeded)
        {
            var inputString = identifier + counter;
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(inputString));
            currentBytes.AddRange(hashBytes);
            counter++;
        }

        for (var i = 0; i < AuthKeyLength; i++)
        {
            // Use the byte to select a character from the lookup
            // We use modulo to ensure it fits within the lookup range
            var lookupIndex = currentBytes[i] % AuthKeyLookup.Count;
            authKey += AuthKeyLookup[(byte)lookupIndex];
        }

        return authKey;
    }

    /// <summary>
    /// Gets a persistent identifier for this machine, preferring MAC address.
    /// </summary>
    private static string GetPersistentIdentifier()
    {
        try
        {
            var macAddress = NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                              && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
                )
                .Select(nic => nic.GetPhysicalAddress().ToString())
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(macAddress))
            {
                return macAddress;
            }
        }
        catch (Exception e)
        {
            Logger.Warn($"Failed to retrieve MAC address: {e}.");
        }

        // Fallback to Unity's Device ID if MAC address fails
        Logger.Warn("Falling back to Device Unique Identifier for AuthKey generation.");
        return SystemInfo.deviceUniqueIdentifier;
    }
}