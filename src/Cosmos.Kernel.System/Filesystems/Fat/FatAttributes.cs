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

    /// <summary>FAT dates count years from 1980 (bits 15-9 of the date word).</summary>
    private const int FatEpochYear = 1980;

    /// <summary>Year field mask after shifting (7 bits, 1980..2107).</summary>
    private const int DateYearMask = 0x7F;

    /// <summary>Month field mask after shifting (bits 8-5).</summary>
    private const int DateMonthMask = 0x0F;

    /// <summary>Day field mask (bits 4-0).</summary>
    private const int DateDayMask = 0x1F;

    /// <summary>Hour field mask after shifting (bits 15-11).</summary>
    private const int TimeHourMask = 0x1F;

    /// <summary>Minute field mask after shifting (bits 10-5).</summary>
    private const int TimeMinuteMask = 0x3F;

    /// <summary>Seconds field mask (bits 4-0), stored at 2-second granularity.</summary>
    private const int TimeTwoSecondMask = 0x1F;

    /// <summary>The stored seconds field counts 2-second units.</summary>
    private const int TwoSecondGranularity = 2;

    /// <summary>The tenths byte counts hundredths 0..199; 100+ rolls into the next second.</summary>
    private const int TenthsPerSecond = 100;

    /// <summary>Nanoseconds per hundredth of a second.</summary>
    private const long NanosecondsPerHundredth = 10_000_000L;

    /// <summary>Seconds per day / hour / minute for the epoch math.</summary>
    private const long SecondsPerDay = 86_400L;
    private const long SecondsPerHour = 3600L;
    private const long SecondsPerMinute = 60L;

    /// <summary>Days per month (non-leap); compiler-emitted static data, no per-call allocation.</summary>
    private static ReadOnlySpan<byte> DaysInMonth => new byte[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

    public static VfsTimespec UnpackDateTime(ushort fatDate, ushort fatTime, byte tenths)
    {
        if (fatDate == 0)
        {
            return new VfsTimespec(0, 0);
        }

        int year = ((fatDate >> 9) & DateYearMask) + FatEpochYear;
        int month = (fatDate >> 5) & DateMonthMask;
        int day = fatDate & DateDayMask;

        int hour = (fatTime >> 11) & TimeHourMask;
        int minute = (fatTime >> 5) & TimeMinuteMask;
        int second = (fatTime & TimeTwoSecondMask) * TwoSecondGranularity + (tenths >= TenthsPerSecond ? 1 : 0);

        long epochSeconds = ToUnixSeconds(year, month, day, hour, minute, second);
        long nanoseconds = (long)(tenths % TenthsPerSecond) * NanosecondsPerHundredth;
        return new VfsTimespec(epochSeconds, nanoseconds);
    }

    private static long ToUnixSeconds(int year, int month, int day, int hour, int minute, int second)
    {
        if (month < 1 || month > 12 || day < 1 || day > 31)
        {
            return 0;
        }

        long days = 0;
        for (int y = 1970; y < year; y++)
        {
            days += IsLeap(y) ? 366 : 365;
        }
        for (int m = 1; m < month; m++)
        {
            days += DaysInMonth[m - 1];
            if (m == 2 && IsLeap(year))
            {
                days++;
            }
        }
        days += day - 1;
        return days * SecondsPerDay + hour * SecondsPerHour + minute * SecondsPerMinute + second;
    }

    private static bool IsLeap(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
    }
}
