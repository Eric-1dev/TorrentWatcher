using System.Security.Cryptography;
using TorrentWatcher.UnitTests.Support;

namespace TorrentWatcher.UnitTests.Domain.Parsing;

/// <summary>
/// Сборка .torrent-фикстур. Структура повторяет реальные rutracker-раздачи: announce,
/// comment со ссылкой на топик, кодировка UTF-8, многофайловый info с кириллицей.
/// Passkey в announce не попадает — настоящие файлы из трекера в репозиторий не кладём.
/// </summary>
internal static class TorrentFixtures
{
    public static readonly byte[] OnePiece = Convert.FromHexString(
        "8f9c4d2a1b7e63f05d4c3b2a1908f7e6d5c4b3a2");

    public static (byte[] Torrent, byte[] Info) SingleFile(
        string name = "ubuntu-24.04.iso",
        long length = 1_048_576,
        bool isPrivate = false)
    {
        byte[] info = BuildInfo(
            name,
            ("length", TestBencode.Integer(length)),
            isPrivate);

        return (BuildTorrent(info), info);
    }

    public static (byte[] Torrent, byte[] Info) MultiFile(
        string name,
        (string Path, long Length)[] files,
        bool isPrivate = false)
    {
        byte[][] fileEntries = new byte[files.Length][];
        for (int i = 0; i < files.Length; i++)
        {
            fileEntries[i] = TestBencode.Dictionary(
                ("length", TestBencode.Integer(files[i].Length)),
                ("path", TestBencode.List(TestBencode.String(files[i].Path))));
        }

        byte[] info = BuildInfo(
            name,
            ("files", TestBencode.List(fileEntries)),
            isPrivate);

        return (BuildTorrent(info), info);
    }

    /// <summary>info с нечитаемым legacy-именем и отдельным name.utf-8 — как в старых раздачах.</summary>
    public static (byte[] Torrent, byte[] Info) SingleFileWithUtf8Name(string utf8Name, byte[] legacyName)
    {
        byte[] info = TestBencode.Dictionary(
            ("length", TestBencode.Integer(512)),
            ("name", TestBencode.String(legacyName)),
            ("name.utf-8", TestBencode.String(utf8Name)),
            ("piece length", TestBencode.Integer(262_144)),
            ("pieces", TestBencode.String(OnePiece)));

        return (BuildTorrent(info), info);
    }

    public static string ExpectedInfoHash(byte[] info) =>
        Convert.ToHexStringLower(SHA1.HashData(info));

    public static byte[] BuildTorrent(byte[] info) =>
        TestBencode.Dictionary(
            ("announce", TestBencode.String("https://rutracker.org/ann?magnet")),
            ("comment", TestBencode.String("https://rutracker.org/forum/viewtopic.php?t=123456")),
            ("created by", TestBencode.String("rutracker.org")),
            ("encoding", TestBencode.String("UTF-8")),
            ("info", info));

    private static byte[] BuildInfo(string name, (string Key, byte[] Value) content, bool isPrivate)
    {
        List<(string Key, byte[] Value)> entries =
        [
            ("name", TestBencode.String(name)),
            ("piece length", TestBencode.Integer(262_144)),
            ("pieces", TestBencode.String(OnePiece)),
            content,
        ];

        if (isPrivate)
        {
            entries.Add(("private", TestBencode.Integer(1)));
        }

        return TestBencode.Dictionary([.. entries]);
    }
}
