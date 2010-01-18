/*
 * Copyright (C) 2008, Robin Rosenberg <robin.rosenberg@dewire.com>
 * Copyright (C) 2008, Shawn O. Pearce <spearce@spearce.org>
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or
 * without modification, are permitted provided that the following
 * conditions are met:
 *
 * - Redistributions of source code must retain the above copyright
 *   notice, this list of conditions and the following disclaimer.
 *
 * - Redistributions in binary form must reproduce the above
 *   copyright notice, this list of conditions and the following
 *   disclaimer in the documentation and/or other materials provided
 *   with the distribution.
 *
 * - Neither the name of the Git Development Community nor the
 *   names of its contributors may be used to endorse or promote
 *   products derived from this software without specific prior
 *   written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
 * NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 * CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT,
 * STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
 * ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.IO;
using GitSharp.Core.Exceptions;
using GitSharp.Core.Util;
using ICSharpCode.SharpZipLib.Tar;

namespace GitSharp.Core.Transport
{

    public class PacketLineIn
    {
        [Serializable]
        public enum AckNackResult
        {
            NAK,
            ACK,
            ACK_CONTINUE,
            ACK_COMMON,
            ACK_READY
        }

        private readonly Stream ins;
        private readonly byte[] lenbuffer;

        public PacketLineIn(Stream i)
        {
            ins = i;
            lenbuffer = new byte[4];
        }

        public Stream sideband(ProgressMonitor pm)
        {
            return new SideBandInputStream(this, ins, pm);
        }

        public AckNackResult readACK(MutableObjectId returnedId)
        {
            string line = ReadString();
            if (line.Length == 0)
                throw new PackProtocolException("Expected ACK/NAK, found EOF");
            if ("NAK".Equals(line))
                return AckNackResult.NAK;
            if (line.StartsWith("ACK "))
            {
                returnedId.FromString(line.Slice(4, 44));


                if (line.Length == 44)
                    return AckNackResult.ACK;

                string arg = line.Substring(44);
                if (arg.Equals(" continue"))
                    return AckNackResult.ACK_CONTINUE;
                else if (arg.Equals(" common"))
                    return AckNackResult.ACK_COMMON;
                else if (arg.Equals(" ready"))
                    return AckNackResult.ACK_READY;
            }
            throw new PackProtocolException("Expected ACK/NAK, got: " + line);
        }

        public string ReadString()
        {
            int len = ReadLength();
            if (len == 0)
                return string.Empty;

            len -= 4; // length header (4 bytes)

            if (len <= 0)
                return string.Empty;

            byte[] raw = new byte[len];

            try
            {
                IO.ReadFully(ins, raw, 0, len);
            }
            catch (IOException e)
            {
                throw invalidHeader(lenbuffer, e);
            }

            if (raw[len - 1] == '\n')
                len--;
            return RawParseUtils.decode(Constants.CHARSET, raw, 0, len);
        }

        public string ReadStringRaw()
        {
            int len = ReadLength();
            if (len == 0)
                return string.Empty;

            len -= 4; // length header (4 bytes)
            if (len == 0)
                return string.Empty;

            byte[] raw = new byte[len];

            try
            {
                IO.ReadFully(ins, raw, 0, len);
            }
            catch (IOException e)
            {
                throw invalidHeader(lenbuffer, e);
            }

            return RawParseUtils.decode(Constants.CHARSET, raw, 0, len);
        }

        public int ReadLength()
        {
            try
            {
                IO.ReadFully(ins, lenbuffer, 0, 4);
            }
            catch (IOException e)
            {
                throw invalidHeader(lenbuffer, e);
            }

            try
            {
                int len = RawParseUtils.parseHexInt16(lenbuffer, 0);
                if (len != 0 && len < 4)
                    throw new IndexOutOfRangeException();
                return len;
            }
            catch (IndexOutOfRangeException e)
            {
                throw invalidHeader(lenbuffer, e);
            }
        }

        private static Exception invalidHeader(byte[] lenbuffer, Exception e)
        {
            return new IOException("Invalid packet line header: " + (char)lenbuffer[0] +
                                                    (char)lenbuffer[1] + (char)lenbuffer[2] + (char)lenbuffer[3], e);
        }
    }


}