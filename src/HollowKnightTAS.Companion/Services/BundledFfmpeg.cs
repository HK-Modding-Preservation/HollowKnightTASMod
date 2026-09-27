using System;
using System.Collections.Generic;
using System.IO;

namespace HollowKnightTAS.Companion.Services
{
    public static class BundledFfmpeg
    {
        public static string Resolve(string? baseDirectory = null)
        {
            var path = Path.GetFullPath(Path.Combine(baseDirectory ?? AppContext.BaseDirectory,
                "Tools", "ffmpeg", "ffmpeg.exe"));
            if (!File.Exists(path))
                throw new FileNotFoundException("内置视频编码器缺失，请重新安装完整的 HollowKnightTAS 发布包。", path);
            return path;
        }

        public static void ApplyDefault(IDictionary<string, string> fields)
        {
            if (!fields.TryGetValue("ffmpegPath", out var path) || string.IsNullOrWhiteSpace(path))
                fields["ffmpegPath"] = Resolve();
        }
    }
}
