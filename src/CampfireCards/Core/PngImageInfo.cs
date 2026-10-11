namespace CampfireCards.Core
{
    /// <summary>Check PNG dimensions before Unity allocates a decoded texture.</summary>
    public static class PngImageInfo
    {
        public const int MaxBytes = 8 * 1024 * 1024;
        public const int MaxDimension = 2048;

        public static bool Supported(byte[] data)
        {
            if (data == null || data.Length < 33 || data.Length > MaxBytes) return false;
            byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < signature.Length; i++) if (data[i] != signature[i]) return false;
            if (Read(data, 8) != 13 || data[12] != 'I' || data[13] != 'H' || data[14] != 'D' || data[15] != 'R') return false;
            uint width = Read(data, 16), height = Read(data, 20);
            return width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension;
        }

        private static uint Read(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
