using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text; // For potential string conversions, though token is binary
using System.Threading;
using System.Threading.Tasks; // For Task.Run if we want to make it async-friendly

public static class HashcashGenerator
{
    // Helper function to convert byte array to Base64Url string
    private static string ToBase64Url(byte[] data)
    {
        var base64 = Convert.ToBase64String(data);
        return base64.Replace('+', '-').Replace('/', '_').Replace("=", "");
    }

    // Helper function to convert Base64Url string to byte array
    private static byte[] FromBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        // Add padding if necessary
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    // Helper to write uint to buffer in little-endian format
    private static void WriteLittleEndianUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    // Helper to read uint from buffer in little-endian format
    private static uint ReadLittleEndianUInt32(byte[] buffer, int offset)
    {
        return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
    }
    private static uint ReadBigEndianUInt32(byte[] buffer, int offset)
    {
        return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
    }

    public static string GenerateHashcashTokenSync(string challenge)
    {
        var parts = challenge.Split(':');
        if (parts.Length < 4) // Need at least version, easiness, (ignored), token
        {
            throw new ArgumentException("Invalid hashcash challenge format. Expected at least 4 parts.");
        }

        if (!int.TryParse(parts[0], out var version) || version != 1)
        {
            throw new ArgumentException("Hashcash challenge version is not 1 or invalid.");
        }

        if (!int.TryParse(parts[1], out var easinessValue) || easinessValue < 0 || easinessValue >= 256)
        {
            // Based on JS comment: "checks if for x = 1 && 0 <= y < 256"
            throw new ArgumentException("Hashcash easiness value is invalid or out of range (0-255).");
        }

        // parts[2] is ignored as per the JS parsing: const [versionStr, easinessStr,, tokenStr]

        var tokenStr = parts[3];
        if (string.IsNullOrEmpty(tokenStr))
        {
            throw new ArgumentException("Hashcash token string is missing.");
        }

        // Calculate threshold (same logic as JS)
        // JS: const base = ((easiness & 63) << 1) + 1
        // JS: const shifts = (easiness >> 6) * 7 + 3
        // JS: const threshold = base << shifts
        // Note: JS numbers are doubles, intermediate calculations can be large.
        // C# int might overflow if not careful, but uint should be fine for threshold.
        var baseCalc = (uint)(((easinessValue & 63) << 1) + 1);
        var shifts = (easinessValue >> 6) * 7 + 3;
        var threshold = baseCalc << shifts;

        var tokenBytes = FromBase64Url(tokenStr);
        // The JS logic `buffer.set(token, 4 + i * 48)` implies tokenBytes.Length is 48.
        // If not, the buffer filling logic might differ or be an error.
        // For direct porting, we assume tokenBytes.Length IS 48.
        if (tokenBytes.Length == 0) // Or check specific length like 48 if it's a strict requirement
        {
            throw new ArgumentException("Decoded token is empty. Expected a non-empty token.");
        }


        const int numTokenCopies = 262144;
        const int tokenCopyLength = 48; // This seems to be the expected length of a segment in JS
        const int counterSizeBytes = 4;

        // Ensure tokenBytes is suitable for copying tokenCopyLength bytes.
        // If tokenBytes is shorter than tokenCopyLength, it should be repeated to fill tokenCopyLength.
        // The JS code `buffer.set(token, 4 + i * 48)` copies the *entire* `token` (tokenBytes)
        // into the buffer at `4 + i * 48`. This implies that `tokenBytes.Length` must be `tokenCopyLength`.
        // If it was shorter, subsequent copies would overwrite previous ones in an undesired way or leave gaps.
        if (tokenBytes.Length != tokenCopyLength)
        {
            // This is a safeguard. If the JS implies token can be shorter and it's tiled within the 48 bytes,
            // this part needs adjustment. But `buffer.set(token, ...)` copies the whole token.
            Console.WriteLine($"Warning: Decoded token length ({tokenBytes.Length}) is not the expected {tokenCopyLength}. Filling might be different than JS if source token is not {tokenCopyLength} bytes.");
            // If the intention is to fill exactly tokenCopyLength bytes using tokenBytes (repeating/truncating tokenBytes if necessary):
            // byte[] effectiveToken = new byte[tokenCopyLength];
            // for(int k=0; k < tokenCopyLength; ++k) effectiveToken[k] = tokenBytes[k % tokenBytes.Length];
            // tokenBytes = effectiveToken; // Now tokenBytes is exactly tokenCopyLength
            // However, the most direct interpretation of `buffer.set(token, ...)` is that token.length IS 48.
        }


        var buffer = new byte[counterSizeBytes + numTokenCopies * tokenCopyLength];

        for (var i = 0; i < numTokenCopies; i++)
        {
            // Array.Copy(sourceArray, sourceIndex, destinationArray, destinationIndex, length)
            // The JS `buffer.set(token, 4 + i * 48)` copies the entire `token` (tokenBytes).
            // So, tokenBytes.Length is copied. If tokenBytes.Length is not 48, this will behave
            // differently than fixed 48-byte copies. Assuming tokenBytes.Length IS 48.
            Array.Copy(tokenBytes, 0, buffer, counterSizeBytes + i * tokenCopyLength, tokenBytes.Length);
        }

        uint counter = 0; // Start counter from 0

        using (var sha256 = SHA256.Create())
        {
            while (true)
            {
                // Place counter (little-endian) into the first 4 bytes of the buffer
                WriteLittleEndianUInt32(buffer, 0, counter);

                // Compute SHA-256 hash of the entire buffer
                var hash = sha256.ComputeHash(buffer);

                // Read the first 4 bytes of the hash as a little-endian unsigned 32-bit integer
                var hashPrefix = ReadBigEndianUInt32(hash, 0);
                //hashPrefix = ReadLittleEndianUInt32(hash, 0);

                if (hashPrefix <= threshold)
                {
                    var solutionCounterBytes = new byte[counterSizeBytes];
                    // Extract the counter that found the solution
                    // This is `buffer.slice(0, 4)` in JS, which contains the current `counter` value.
                    Array.Copy(buffer, 0, solutionCounterBytes, 0, counterSizeBytes);
                    return $"1:{tokenStr}:{ToBase64Url(solutionCounterBytes)}";
                }

                counter++;
                // Check for counter overflow (extremely unlikely to happen before a solution is found for typical difficulties)
                if (counter == 0)
                {
                    throw new OverflowException("Hashcash counter overflowed. No solution found within 2^32 iterations.");
                }
            }
        }
    }

    // Async wrapper for CPU-bound work, good practice for libraries
    public static Task<string> GenerateHashcashTokenAsync(string challenge)
    {
        // Offload the CPU-bound work to a thread pool thread
        return Task.Run(() => GenerateHashcashTokenSync(challenge));
    }
}


public static class OptimizedHashcashGenerator
{
    private static string ToBase64Url(byte[] data)
    {
        string base64 = Convert.ToBase64String(data);
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static byte[] FromBase64Url(string base64Url)
    {
        string base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    private static void WriteLittleEndianUInt32(Span<byte> buffer, int offset, uint value) // Changed to Span<byte>
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
    // Overload for byte[] for solutionCounterBytes, or convert solutionCounterBytes to Span
    private static void WriteLittleEndianUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }


    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> buffer, int offset)
    {
        return (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3]);
    }

    public static string GenerateHashcashTokenSync(string challenge)
    {
        string[] parts = challenge.Split(':');
        if (parts.Length < 4) throw new ArgumentException("Invalid challenge format.");
        if (!int.TryParse(parts[0], out int version) || version != 1) throw new ArgumentException("Version not 1.");
        if (!int.TryParse(parts[1], out int easinessValue) || easinessValue < 0 || easinessValue >= 256) throw new ArgumentException("Easiness out of range.");
        string tokenStr = parts[3];
        if (string.IsNullOrEmpty(tokenStr)) throw new ArgumentException("Token string missing.");

        uint baseCalc = (uint)(((easinessValue & 63) << 1) + 1);
        int shifts = (easinessValue >> 6) * 7 + 3;
        uint threshold = baseCalc << shifts;

        byte[] tokenBytes = FromBase64Url(tokenStr);
        const int numTokenCopies = 262144;
        int actualTokenLengthPerCopy = tokenBytes.Length;
        const int counterSizeBytes = 4;

        byte[] staticTokenData = new byte[numTokenCopies * actualTokenLengthPerCopy];
        for (int i = 0; i < numTokenCopies; i++)
        {
            Array.Copy(tokenBytes, 0, staticTokenData, i * actualTokenLengthPerCopy, actualTokenLengthPerCopy);
        }


        string? solution = null;
        CancellationTokenSource cts = new CancellationTokenSource();
        object lockObj = new object();

        int degreeOfParallelism = Environment.ProcessorCount;
        long startCounter = 0L;
        long endCounterExclusive = (long)uint.MaxValue + 1; // Iterate 0 to uint.MaxValue inclusive

        try
        {
            //Using dynamic partitioning, which is generally robust
            Parallel.ForEach(Partitioner.Create(startCounter, endCounterExclusive),
                new ParallelOptions { MaxDegreeOfParallelism = degreeOfParallelism, CancellationToken = cts.Token },
                (range, loopState) => // range is Tuple<long, long> (fromInclusive, toExclusive)
                {                    
                    // Each thread needs its own IncrementalHash instance and buffer for the counter
                    using (IncrementalHash ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        Span<byte> counterBufferSpan = stackalloc byte[counterSizeBytes]; // For current counter
                        Span<byte> hashDestination = stackalloc byte[32]; // SHA256.HashSizeInBytes                      

                        for (long i = range.Item1; i < range.Item2; i++)
                        {
                            if (cts.IsCancellationRequested) break;

                            uint currentCounter = (uint)i;
                            WriteLittleEndianUInt32(counterBufferSpan, 0, currentCounter);

                            ih.AppendData(counterBufferSpan);       // Append counter (4 bytes)
                            ih.AppendData(staticTokenData);     // Append shared static data (large)

                            if (!ih.TryGetHashAndReset(hashDestination, out int bytesWrittenInc) || bytesWrittenInc != hashDestination.Length)
                                throw new CryptographicException("IncrementalHash computation failed.");

                            uint hashPrefix = ReadBigEndianUInt32(hashDestination, 0);

                            if (hashPrefix <= threshold)
                            {
                                byte[] solutionCounterBytes = new byte[counterSizeBytes];
                                WriteLittleEndianUInt32(solutionCounterBytes, 0, currentCounter);

                                bool solutionFoundThisThread = false;
                                lock (lockObj)
                                {
                                    if (solution == null)
                                    {
                                        solution = $"1:{tokenStr}:{ToBase64Url(solutionCounterBytes)}";
                                        solutionFoundThisThread = true;
                                    }
                                }
                                if (solutionFoundThisThread)
                                {
                                    cts.Cancel();
                                }
                                loopState.Stop();
                                break;
                            }
                        }
                    }
                }
            );
        }
        catch (OperationCanceledException)
        {
        }

        cts.Dispose();
        if (solution == null && !cts.IsCancellationRequested) // Check if cancellation was due to finding a solution or timeout/other reason
        {
            // If cts.IsCancellationRequested is true here, it means a solution was likely found,
            // or some other external cancellation occurred.
            // If solution is still null AND it wasn't a normal cancellation (e.g. from finding a solution),
            // then it's an overflow/no solution found.
            // However, a simpler check is if solution is null after the parallel loop completes.
            throw new OverflowException("Hashcash counter overflowed across all threads or no solution found in range.");
        }
        if (solution == null)
        {
            // This case might be hit if cancellation happened for other reasons or if the loop finished without solution
            // (e.g., if endCounterExclusive was smaller and no solution found in that smaller range).
            // For the full uint.MaxValue range, this means "not found".
            throw new InvalidOperationException("No solution found within the counter range, or operation was cancelled before solution found.");
        }
        return solution;
    }

    public static Task<string> GenerateHashcashTokenAsync(string challenge)
    {
        return Task.Run(() => GenerateHashcashTokenSync(challenge));
    }
}
