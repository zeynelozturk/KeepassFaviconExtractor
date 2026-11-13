using System;
using System.Linq;
using KeePassLib;

namespace FaviconExtractor
{
    // Snapshot the exact entry and icon state; never resolve the target from later UI selection.
    internal sealed class IconChoiceTarget
    {
        public PwDatabase Database { get; private set; }
        public PwEntry Entry { get; private set; }
        private readonly PwUuid customIconUuid;
        private readonly PwIcon builtInIcon;
        public byte[] OriginalPng { get; private set; }
        public string OriginalKey { get; private set; }

        public IconChoiceTarget(PwDatabase database, PwEntry entry)
        {
            Database = database;
            Entry = entry;
            customIconUuid = entry.CustomIconUuid;
            builtInIcon = entry.IconId;
            OriginalPng = GetIconPng();
            if (OriginalPng != null) OriginalPng = (byte[])OriginalPng.Clone();
            OriginalKey = PreparedIcon.GetContentKey(OriginalPng);
        }

        private byte[] GetIconPng()
        {
            var icon = Database.CustomIcons.FirstOrDefault(i => i.Uuid.Equals(Entry.CustomIconUuid));
            return icon != null ? icon.ImageDataPng : null;
        }

        public void Validate()
        {
            if (!Database.IsOpen || Database.RootGroup == null ||
                !ReferenceEquals(Database.RootGroup.FindEntry(Entry.Uuid, true), Entry))
                throw new InvalidOperationException("The original database or entry is no longer available. Open the chooser again for a valid entry.");
            for (PwGroup group = Entry.ParentGroup; group != null; group = group.ParentGroup)
                if (!Database.RecycleBinUuid.IsZero && group.Uuid.Equals(Database.RecycleBinUuid))
                    throw new InvalidOperationException("The entry is in the recycle bin. Restore it before choosing an icon.");
            if (!customIconUuid.Equals(Entry.CustomIconUuid) || builtInIcon != Entry.IconId ||
                OriginalKey != PreparedIcon.GetContentKey(GetIconPng()))
                throw new InvalidOperationException("The entry icon changed while the chooser was open. Open the chooser again to use its current icon.");
        }
    }
}
