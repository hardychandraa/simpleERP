using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SimpleERP.Web.Services;

/// <summary>
/// Encrypted backup files (HC, 2026-10-22, option A): a pg_dump file anyone could read became
/// <c>.dump.enc</c>, readable only with the backup passphrase. The passphrase is a service
/// setting (<c>Backup__Passphrase</c>) and lives in HC's password manager; without it the files
/// are useless, so losing it loses every encrypted backup.
///
/// AES-256-GCM, built into .NET. The key comes from the passphrase through PBKDF2-SHA256 with a
/// random salt per file. The file is cut into 1 MiB chunks so a large dump never sits in memory;
/// each chunk carries its own tag, and its number and a "last chunk" flag are authenticated, so
/// reordered, removed or appended chunks are refused just like a changed byte.
///
/// Format v1: "SERPBAK" | version 1 | iterations (int32) | salt (16) | nonce prefix (4) | chunk
/// size (int32), then per chunk: flags (1 = last) | length (int32) | ciphertext | tag (16).
/// Nonce = prefix | chunk number (int64). AAD = header | chunk number | flags | length.
/// </summary>
public static class BackupCrypto
{
    public const string Extension = ".enc";
    public const int MinPassphraseLength = 16;
    /// <summary>The service setting and, for the command line, the environment variable.</summary>
    public const string PassphraseVariable = "Backup__Passphrase";

    private static readonly byte[] Magic = "SERPBAK"u8.ToArray();
    private const byte Version          = 1;
    private const int  Iterations       = 600_000;
    private const int  SaltSize         = 16;
    private const int  NoncePrefixSize  = 4;
    private const int  TagSize          = 16;
    private const int  KeySize          = 32;
    private const int  ChunkSize        = 1 << 20;
    private const int  HeaderSize       = 7 + 1 + 4 + SaltSize + NoncePrefixSize + 4;
    private const byte LastChunk        = 1;

    public static void Encrypt(Stream input, Stream output, string passphrase)
    {
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[7] = Version;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), Iterations);
        RandomNumberGenerator.Fill(header.AsSpan(12, SaltSize));
        RandomNumberGenerator.Fill(header.AsSpan(12 + SaltSize, NoncePrefixSize));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12 + SaltSize + NoncePrefixSize), ChunkSize);
        output.Write(header);

        using var aes = new AesGcm(DeriveKey(passphrase, header.AsSpan(12, SaltSize), Iterations), TagSize);
        var plain  = new byte[ChunkSize];
        var next   = new byte[ChunkSize];
        var cipher = new byte[ChunkSize];
        var tag    = new byte[TagSize];
        var n = ReadFull(input, plain);
        for (long index = 0; ; index++)
        {
            // Read one chunk ahead: only then is it known whether this one is the last.
            var m    = n == ChunkSize ? ReadFull(input, next) : 0;
            var last = m == 0;
            var flags = last ? LastChunk : (byte)0;

            aes.Encrypt(Nonce(header, index), plain.AsSpan(0, n), cipher.AsSpan(0, n), tag,
                        Aad(header, index, flags, n));
            var record = new byte[5];
            record[0] = flags;
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(1), n);
            output.Write(record);
            output.Write(cipher, 0, n);
            output.Write(tag);

            if (last) break;
            (plain, next) = (next, plain);
            n = m;
        }
    }

    /// <summary>
    /// Throws <see cref="CryptographicException"/> for a wrong passphrase or a changed file and
    /// <see cref="InvalidDataException"/> for something that isn't one of these files at all.
    /// Chunks before a damaged one may already be written: use <see cref="DecryptFile"/>.
    /// </summary>
    public static void Decrypt(Stream input, Stream output, string passphrase)
    {
        var header = new byte[HeaderSize];
        if (ReadFull(input, header) != HeaderSize || !header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This is not an encrypted SimpleERP backup.");
        if (header[7] != Version)
            throw new InvalidDataException($"Unknown backup format version {header[7]}: a newer SimpleERP made it.");
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        var chunkSize  = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12 + SaltSize + NoncePrefixSize));
        if (iterations is < 100_000 or > 10_000_000 || chunkSize is <= 0 or > 64 << 20)
            throw new InvalidDataException("The backup header is damaged.");

        using var aes = new AesGcm(DeriveKey(passphrase, header.AsSpan(12, SaltSize), iterations), TagSize);
        var record = new byte[5];
        var cipher = new byte[chunkSize];
        var plain  = new byte[chunkSize];
        var tag    = new byte[TagSize];
        for (long index = 0; ; index++)
        {
            if (ReadFull(input, record) != record.Length)
                throw new CryptographicException("The backup file is incomplete (cut short).");
            var flags = record[0];
            var n     = BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1));
            if (flags > LastChunk || n < 0 || n > chunkSize)
                throw new CryptographicException("The backup file is damaged.");
            if (ReadFull(input, cipher.AsSpan(0, n)) != n || ReadFull(input, tag) != TagSize)
                throw new CryptographicException("The backup file is incomplete (cut short).");
            try
            {
                aes.Decrypt(Nonce(header, index), cipher.AsSpan(0, n), tag, plain.AsSpan(0, n),
                            Aad(header, index, flags, n));
            }
            catch (CryptographicException)
            {
                throw new CryptographicException(index == 0
                    ? "Wrong passphrase, or the backup file is damaged."
                    : "The backup file is damaged.");
            }
            output.Write(plain, 0, n);
            if (flags == LastChunk)
            {
                if (input.ReadByte() != -1) throw new CryptographicException("The backup file has extra data after its end.");
                return;
            }
        }
    }

    /// <summary>Encrypts to <paramref name="dest"/>; a failure leaves no file behind.</summary>
    public static void EncryptFile(string source, string dest, string passphrase) =>
        WriteVia(dest, output => { using var input = File.OpenRead(source); Encrypt(input, output, passphrase); });

    /// <summary>Decrypts to <paramref name="dest"/>; a failure leaves no partial dump behind.</summary>
    public static void DecryptFile(string source, string dest, string passphrase) =>
        WriteVia(dest, output => { using var input = File.OpenRead(source); Decrypt(input, output, passphrase); });

    /// <summary>SHA-256 of the decrypted content, without writing it anywhere.</summary>
    public static byte[] DecryptedSha256(string source, string passphrase)
    {
        using var sha    = SHA256.Create();
        using var hashed = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        using (var input = File.OpenRead(source)) Decrypt(input, hashed, passphrase);
        hashed.FlushFinalBlock();
        return sha.Hash!;
    }

    /// <summary>
    /// <c>SimpleERP.Web.exe decrypt-backup &lt;file.dump.enc&gt; &lt;out.dump&gt;</c> (and
    /// <c>encrypt-backup</c> for a dump taken by hand). Runs before anything else in Program.cs:
    /// no database, no web server. The passphrase comes from the Backup__Passphrase environment
    /// variable, otherwise it is asked for without echoing it.
    /// </summary>
    public static int RunCli(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: SimpleERP.Web.exe decrypt-backup <file.dump.enc> <out.dump>");
            Console.Error.WriteLine("       SimpleERP.Web.exe encrypt-backup <file.dump> <out.dump.enc>");
            return 2;
        }
        var (command, source, dest) = (args[0], args[1], args[2]);
        if (!File.Exists(source)) { Console.Error.WriteLine($"FAILED: {source} not found."); return 2; }
        if (File.Exists(dest))    { Console.Error.WriteLine($"FAILED: {dest} already exists; it was not overwritten."); return 2; }

        var passphrase = Environment.GetEnvironmentVariable(PassphraseVariable);
        if (string.IsNullOrEmpty(passphrase)) passphrase = ReadHidden("Backup passphrase: ");
        try
        {
            if (command == "encrypt-backup")
            {
                if (passphrase.Length < MinPassphraseLength)
                {
                    Console.Error.WriteLine($"FAILED: the passphrase must be at least {MinPassphraseLength} characters.");
                    return 2;
                }
                EncryptFile(source, dest, passphrase);
            }
            else DecryptFile(source, dest, passphrase);
            Console.WriteLine($"OK: {dest} ({new FileInfo(dest).Length:N0} bytes)");
            return 0;
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"FAILED: {ex.Message}");
            return 1;
        }
    }

    private static void WriteVia(string dest, Action<Stream> write)
    {
        var part = dest + ".part";
        try
        {
            using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write))
                write(output);
            File.Move(part, dest);
        }
        finally
        {
            if (File.Exists(part)) File.Delete(part);
        }
    }

    private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations, HashAlgorithmName.SHA256, KeySize);

    private static byte[] Nonce(byte[] header, long index)
    {
        var nonce = new byte[NoncePrefixSize + 8];
        header.AsSpan(12 + SaltSize, NoncePrefixSize).CopyTo(nonce);
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(NoncePrefixSize), index);
        return nonce;
    }

    private static byte[] Aad(byte[] header, long index, byte flags, int length)
    {
        var aad = new byte[HeaderSize + 8 + 1 + 4];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteInt64BigEndian(aad.AsSpan(HeaderSize), index);
        aad[HeaderSize + 8] = flags;
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(HeaderSize + 9), length);
        return aad;
    }

    private static int ReadFull(Stream input, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = input.Read(buffer[total..]);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Error.Write(prompt);
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); }
            else if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
        }
        Console.Error.WriteLine();
        return new string(chars.ToArray());
    }
}
