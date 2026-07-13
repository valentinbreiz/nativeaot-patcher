// Minimal replacement for SharpZipLib's exception hierarchy: the vendored
// inflater classes (see docs/credits.md) only ever construct this type with a
// message, so the serialization constructors of the original are not needed.
using System;

namespace ICSharpCode.SharpZipLib
{
    /// <summary>
    /// Base exception for corrupt or unsupported compressed data.
    /// </summary>
    public class SharpZipBaseException : Exception
    {
        /// <summary>
        /// Initializes a new instance with a message describing the error.
        /// </summary>
        /// <param name="message">A message describing the error.</param>
        public SharpZipBaseException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Indicates that an error occurred while decoding a compressed stream.
    /// </summary>
    public class StreamDecodingException : SharpZipBaseException
    {
        /// <summary>
        /// Initializes a new instance with a message describing the error.
        /// </summary>
        /// <param name="message">A message describing the error.</param>
        public StreamDecodingException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Indicates that a value read from a compressed stream is out of its legal range.
    /// </summary>
    public class ValueOutOfRangeException : StreamDecodingException
    {
        /// <summary>
        /// Initializes a new instance naming the out-of-range value.
        /// </summary>
        /// <param name="nameOfValue">Name of the value that was out of range.</param>
        public ValueOutOfRangeException(string nameOfValue) : base($"{nameOfValue} out of range")
        {
        }
    }
}
