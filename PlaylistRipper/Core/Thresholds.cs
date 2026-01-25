namespace PlaylistRipper.Core;

public static class Thresholds
{
    public static bool WouldExceedZipThreshold(long stagingBytes, long nextBytes, long zipThresholdBytes)
    {
        if (zipThresholdBytes <= 0) return false;

        if (nextBytes <= 0)
            return stagingBytes >= zipThresholdBytes;

        return (stagingBytes + nextBytes) > zipThresholdBytes;
    }
}
