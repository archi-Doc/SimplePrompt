// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

namespace SimplePrompt;

internal static partial class TermInfo
{
    internal static class DatabaseFactory
    {
        /// <summary>
        /// The default locations in which to search for terminfo databases.
        /// This is the ordering of well-known locations used by ncurses.
        /// </summary>
        private static readonly string[] SystemTermInfoLocations = [
            "/etc/terminfo",
            "/lib/terminfo",
            "/usr/share/terminfo",
            "/usr/share/misc/terminfo",
            "/usr/local/share/terminfo",
        ];

        private static string? HomeTermInfoLocation
        {
            get
            {
                string? home = Environment.GetEnvironmentVariable("HOME");
                return home is null ? null : home + "/.terminfo";
            }
        }

        /// <summary>Read the database for the current terminal as specified by the "TERM" environment variable.</summary>
        /// <returns>The database, or null if it could not be found.</returns>
        internal static Database? ReadActiveDatabase()
        {
            string? term = Environment.GetEnvironmentVariable("TERM");
            return !string.IsNullOrEmpty(term) ? ReadDatabase(term) : null;
        }

        /// <summary>Read the database for the specified terminal.</summary>
        /// <param name="term">The identifier for the terminal.</param>
        /// <returns>The database, or null if it could not be found.</returns>
        private static Database? ReadDatabase(string term)
        {
            Database? db;
            var terminfo = Environment.GetEnvironmentVariable("TERMINFO");
            if ((db = ReadDatabase(term, terminfo)) != null)
            {
                return db;
            }

            terminfo = HomeTermInfoLocation;
            if ((db = ReadDatabase(term, terminfo)) != null)
            {
                return db;
            }

            foreach (string terminfoLocation in SystemTermInfoLocations)
            {
                if ((db = ReadDatabase(term, terminfoLocation)) != null)
                {
                    return db;
                }
            }

            return null;
        }

        /// <summary>Attempt to open as readonly the specified file path.</summary>
        /// <param name="filePath">The path to the file to open.</param>
        /// <param name="fd">If successful, the opened file descriptor; otherwise, -1.</param>
        /// <returns>true if the file was successfully opened; otherwise, false.</returns>
        private static bool TryOpen(string filePath, [NotNullWhen(true)] out SafeFileHandle? fd)
        {
            fd = Interop.Sys.Open(filePath, Interop.OpenFlags.O_RDONLY | Interop.OpenFlags.O_CLOEXEC, 0);
            if (fd.IsInvalid)
            {
                // Don't throw in this case, as we'll be polling multiple locations looking for the file.
                fd.Dispose();
                fd = null;
                return false;
            }

            return true;
        }

        /// <summary>Read the database for the specified terminal from the specified directory.</summary>
        /// <param name="term">The identifier for the terminal.</param>
        /// <param name="directoryPath">The path to the directory containing terminfo database files.</param>
        /// <returns>The database, or null if it could not be found.</returns>
        private static Database? ReadDatabase(string? term, string? directoryPath)
        {
            if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(directoryPath))
            {
                return null;
            }

            Span<char> stackBuffer = stackalloc char[256];
            SafeFileHandle? fd;
            if (!TryOpen(string.Create(null, stackBuffer, $"{directoryPath}/{term[0]}/{term}"), out fd) &&
                !TryOpen(string.Create(null, stackBuffer, $"{directoryPath}/{(int)term[0]:X}/{term}"), out fd))
            {
                return null;
            }

            using (fd)
            {
                // Read in all of the terminfo data
                long termInfoLength = RandomAccess.GetLength(fd);
                const int HeaderLength = 12;
                if (termInfoLength <= HeaderLength)
                {
                    throw new InvalidOperationException();
                }

                byte[] data = new byte[(int)termInfoLength];
                long fileOffset = 0;
                do
                {
                    int bytesRead = RandomAccess.Read(fd, new Span<byte>(data, (int)fileOffset, (int)(termInfoLength - fileOffset)), fileOffset);
                    if (bytesRead == 0)
                    {
                        throw new InvalidOperationException();
                    }

                    fileOffset += bytesRead;
                }
                while (fileOffset < termInfoLength);

                // Create the database from the data
                return new Database(term, data);
            }
        }
    }
}
