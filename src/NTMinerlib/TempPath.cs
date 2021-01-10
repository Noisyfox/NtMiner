using System;
using System.IO;

namespace NTMiner {
    public static class TempPath {
        public static readonly string TempDirFullName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
    }
}
