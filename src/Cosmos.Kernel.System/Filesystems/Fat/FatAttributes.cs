// This code is licensed under MIT license (see LICENSE for details)

using Cosmos.Kernel.HAL.Vfs;

namespace Cosmos.Kernel.System.Filesystems.Fat;

/// <summary>
/// FAT attribute byte / date / time conversions for the VFS layer.
/// </summary>
internal static class FatAttributes
{
    // FAT predates POSIX permissions; we synthesize 0o755 / 0o644 and clear
    // write bits when ATTR_READ_ONLY is set so callers see plausible mode.
    private const ModeEnum DirPermissions =
        ModeEnum.OwnerRead | ModeEnum.OwnerWrite | ModeEnum.OwnerExecute |
        ModeEnum.GroupRead | ModeEnum.GroupExecute |
        ModeEnum.OtherRead | ModeEnum.OtherExecute;

    private const ModeEnum FilePermissions =
        ModeEnum.OwnerRead | ModeEnum.OwnerWrite |
        ModeEnum.GroupRead |
        ModeEnum.OtherRead;

    private const ModeEnum WriteMask =
        ModeEnum.OwnerWrite | ModeEnum.GroupWrite | ModeEnum.OtherWrite;

    public static ModeEnum ToMode(FatAttr attributes)
    {
        bool isDir = (attributes & FatAttr.Directory) != 0;
        ModeEnum mode = isDir
            ? ModeEnum.Directory | DirPermissions
            : ModeEnum.RegularFile | FilePermissions;

        if ((attributes & FatAttr.ReadOnly) != 0)
        {
            mode &= ~WriteMask;
        }

        return mode;
    }

    public static FatAttr ToFatAttr(ModeEnum mode)
    {
        FatAttr attr = (mode & ModeEnum.FileTypeMask) == ModeEnum.Directory
            ? FatAttr.Directory
            : FatAttr.None;

        if ((mode & ModeEnum.OwnerWrite) == 0 && (mode & ModeEnum.FileTypeMask) != ModeEnum.Directory)
        {
            attr |= FatAttr.ReadOnly;
        }

        return attr;
    }

    public static VfsTimespec UnpackDateTime(ushort fatDate, ushort fatTime, byte tenths)
    {
        if (fatDate == 0)
        {
            return new VfsTimespec(0, 0);
        }

        int year = ((fatDate >> 9) & 0x7F) + 1980;
        int month = (fatDate >> 5) & 0x0F;
        int day = fatDate & 0x1F;

        int hour = (fatTime >> 11) & 0x1F;
        int minute = (fatTime >> 5) & 0x3F;
        int second = (fatTime & 0x1F) * 2 + (tenths >= 100 ? 1 : 0);

        long epochSeconds = ToUnixSeconds(year, month, day, hour, minute, second);
        long nanoseconds = (long)(tenths % 100) * 10_000_000L;
        return new VfsTimespec(epochSeconds, nanoseconds);
    }

    private static long ToUnixSeconds(int year, int month, int day, int hour, int minute, int second)
    {
        if (month < 1 || month > 12 || day < 1 || day > 31)
        {
            return 0;
        }

        int[] daysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        long days = 0;
        for (int y = 1970; y < year; y++)
        {
            days += IsLeap(y) ? 366 : 365;
        }
        for (int m = 1; m < month; m++)
        {
            days += daysInMonth[m - 1];
            if (m == 2 && IsLeap(year))
            {
                days++;
            }
        }
        days += day - 1;
        return days * 86_400L + hour * 3600L + minute * 60L + second;
    }

    private static bool IsLeap(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }
}
