using System;
using System.Security.Cryptography;
using System.Text;

namespace NzbDrone.Core.Indexers.Slskd
{
    /// <summary>
    /// slskd has no per-grab identifier, so a download is identified by the peer and the folder
    /// the files came from. The id has to be the same string every time it is derived - the
    /// indexer stamps it on the release, the client returns it from Download and reconstructs it
    /// from the transfer list on every poll - and it used to be built from string.GetHashCode(),
    /// which .NET randomises per process. Every container restart therefore changed every id,
    /// and a grab made before a restart could never be matched to its completion afterwards.
    /// </summary>
    public static class SlskdIds
    {
        public static string ForDownload(string username, string directory)
        {
            return $"Slskd-{username}-{Hash(directory)}";
        }

        private static string Hash(string value)
        {
            using (var sha1 = SHA1.Create())
            {
                var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));

                return BitConverter.ToString(bytes, 0, 4).Replace("-", string.Empty);
            }
        }
    }
}
