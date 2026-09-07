using System;
using KeePassLib;

namespace FaviconExtractor
{
    internal static class KeePassIconAssigner
    {
        public static PwUuid AssignNormalizedPngToEntry(PwDatabase database, PwEntry entry, byte[] normalizedPng)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (normalizedPng == null || normalizedPng.Length == 0)
            {
                throw new ArgumentException("Normalized PNG bytes are empty.", nameof(normalizedPng));
            }

            PwUuid targetUuid = FindMatchingCustomIconUuid(database, normalizedPng);
            bool iconCreated = false;

            if (targetUuid == null || targetUuid.IsZero)
            {
                targetUuid = new PwUuid(true);
                database.CustomIcons.Add(new PwCustomIcon(targetUuid, normalizedPng));
                iconCreated = true;
            }

            bool entryChanged = entry.CustomIconUuid == null
                || entry.CustomIconUuid.IsZero
                || !entry.CustomIconUuid.Equals(targetUuid);

            if (entryChanged)
            {
                entry.CustomIconUuid = targetUuid;
                entry.Touch(true);
            }

            if (iconCreated || entryChanged)
            {
                database.Modified = true;
            }

            return targetUuid;
        }

        private static PwUuid FindMatchingCustomIconUuid(PwDatabase database, byte[] normalizedPng)
        {
            foreach (PwCustomIcon customIcon in database.CustomIcons)
            {
                if (customIcon == null || customIcon.ImageDataPng == null)
                {
                    continue;
                }

                if (ByteArraysEqual(customIcon.ImageDataPng, normalizedPng))
                {
                    return customIcon.Uuid;
                }
            }

            return null;
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
