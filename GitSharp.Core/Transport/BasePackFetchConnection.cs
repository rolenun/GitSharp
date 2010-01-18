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
using System.Collections.Generic;
using System.IO;
using System.Text;
using GitSharp.Core.Exceptions;
using GitSharp.Core.RevWalk;
using GitSharp.Core.RevWalk.Filter;

namespace GitSharp.Core.Transport
{
    public class BasePackFetchConnection : BasePackConnection, IFetchConnection, IDisposable
    {
        private const int MAX_HAVES = 256;
        protected const int MIN_CLIENT_BUFFER = 2 * 32 * 46 + 8;
        public const string OPTION_INCLUDE_TAG = "include-tag";
        public const string OPTION_MULTI_ACK = "multi_ack";
        public const string OPTION_MULTI_ACK_DETAILED = "multi_ack_detailed";
        public const string OPTION_THIN_PACK = "thin-pack";
        public const string OPTION_SIDE_BAND = "side-band";
        public const string OPTION_SIDE_BAND_64K = "side-band-64k";
        public const string OPTION_OFS_DELTA = "ofs-delta";
        public const string OPTION_SHALLOW = "shallow";
        public const string OPTION_NO_PROGRESS = "no-progress";

        public enum MultiAck
        {
            OFF,
            CONTINUE,
            DETAILED
        }

        private readonly RevWalk.RevWalk _walk;
        private readonly bool _allowOfsDelta;

        public readonly RevFlag REACHABLE;
        public readonly RevFlag COMMON;
        public readonly RevFlag ADVERTISED;
        private RevCommitList<RevCommit> _reachableCommits;
        private MultiAck _multiAck = MultiAck.OFF;
        private bool _thinPack;
        private bool _sideband;
        private bool _includeTags;
        private string _lockMessage;
        private PackLock _packLock;

        public BasePackFetchConnection(IPackTransport packTransport)
            : base(packTransport)
        {
            RepositoryConfig cfg = local.Config;
            _includeTags = transport.TagOpt != TagOpt.NO_TAGS;
            _thinPack = transport.FetchThin;
            _allowOfsDelta = cfg.getBoolean("repack", "usedeltabaseoffset", true);

            _walk = new RevWalk.RevWalk(local);
            _reachableCommits = new RevCommitList<RevCommit>();
            REACHABLE = _walk.newFlag("REACHABLE");
            COMMON = _walk.newFlag("COMMON");
            ADVERTISED = _walk.newFlag("ADVERTISED");

            _walk.carry(COMMON);
            _walk.carry(REACHABLE);
            _walk.carry(ADVERTISED);
        }

        public void Fetch(ProgressMonitor monitor, List<Ref> want, List<ObjectId> have)
        {
            markStartedOperation();
            doFetch(monitor, want, have);
        }

        public bool DidFetchIncludeTags
        {
            get { return false; }
        }

        public bool DidFetchTestConnectivity
        {
            get { return false; }
        }

        public void SetPackLockMessage(string message)
        {
            _lockMessage = message;
        }

        public List<PackLock> PackLocks
        {
            get { return _packLock != null ? new List<PackLock> { _packLock } : new List<PackLock>(); }
        }

        protected void doFetch(ProgressMonitor monitor, List<Ref> want, List<ObjectId> have)
        {
            try
            {
                MarkRefsAdvertised();
                MarkReachable(have, MaxTimeWanted(want));

                if (SendWants(want))
                {
                    Negotiate(monitor);

                    _walk.Dispose();
                    _reachableCommits = null;

                    ReceivePack(monitor);
                }
            }
            catch (CancelledException)
            {
                Dispose();
                return;
            }
            catch (IOException err)
            {
                Dispose();
                throw new TransportException(err.Message, err);
            }
        }

        private int MaxTimeWanted(IEnumerable<Ref> wants)
        {
            int maxTime = 0;
            foreach (Ref r in wants)
            {
                try
                {
                    RevCommit obj = (_walk.parseAny(r.ObjectId) as RevCommit);
                    if (obj != null)
                    {
                        int cTime = obj.CommitTime;
                        if (maxTime < cTime)
                            maxTime = cTime;
                    }
                }
                catch (IOException)
                {
                }
            }

            return maxTime;
        }

        private void MarkReachable(IEnumerable<ObjectId> have, int maxTime)
        {
            foreach (Ref r in local.getAllRefs().Values)
            {
                try
                {
                    RevCommit o = _walk.parseCommit(r.ObjectId);
                    o.add(REACHABLE);
                    _reachableCommits.add(o);
                }
                catch (IOException)
                {
                }
            }

            foreach (ObjectId id in have)
            {
                try
                {
                    RevCommit o = _walk.parseCommit(id);
                    o.add(REACHABLE);
                    _reachableCommits.add(o);
                }
                catch (IOException)
                {
                }
            }

            if (maxTime > 0)
            {
                DateTime maxWhen = new DateTime(1970, 1, 1) + TimeSpan.FromSeconds(maxTime);
                _walk.sort(RevSort.COMMIT_TIME_DESC);
                _walk.markStart(_reachableCommits);
                _walk.setRevFilter(CommitTimeRevFilter.After(maxWhen));
                for (; ; )
                {
                    RevCommit c = _walk.next();
                    if (c == null)
                        break;
                    if (c.has(ADVERTISED) && !c.has(COMMON))
                    {
                        c.add(COMMON);
                        c.carry(COMMON);
                        _reachableCommits.add(c);
                    }
                }
            }
        }

        private bool SendWants(IEnumerable<Ref> want)
        {
            bool first = true;
            foreach (Ref r in want)
            {
                try
                {
                    if (_walk.parseAny(r.ObjectId).has(REACHABLE))
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                }

                var line = new StringBuilder(46);
                line.Append("want ");
                line.Append(r.ObjectId.Name);
                if (first)
                {
                    line.Append(EnableCapabilities());
                    first = false;
                }
                line.Append('\n');
                pckOut.WriteString(line.ToString());
            }
            pckOut.End();
            outNeedsEnd = false;
            return !first;
        }

        private string EnableCapabilities()
        {
            var line = new StringBuilder();
            if (_includeTags)
                _includeTags = wantCapability(line, OPTION_INCLUDE_TAG);
            if (_allowOfsDelta)
                wantCapability(line, OPTION_OFS_DELTA);

            if (wantCapability(line, OPTION_MULTI_ACK_DETAILED))
                _multiAck = MultiAck.DETAILED;
            else if (wantCapability(line, OPTION_MULTI_ACK))
                _multiAck = MultiAck.CONTINUE;
            else
                _multiAck = MultiAck.OFF;

            if (_thinPack)
                _thinPack = wantCapability(line, OPTION_THIN_PACK);
            if (wantCapability(line, OPTION_SIDE_BAND_64K))
                _sideband = true;
            else if (wantCapability(line, OPTION_SIDE_BAND))
                _sideband = true;
            return line.ToString();
        }

        private void Negotiate(ProgressMonitor monitor)
        {
            var ackId = new MutableObjectId();
            int resultsPending = 0;
            int havesSent = 0;
            int havesSinceLastContinue = 0;
            bool receivedContinue = false;
            bool receivedAck = false;

            NegotiateBegin();
            for (; ; )
            {
                RevCommit c = _walk.next();
                if (c == null)
                {
                    goto END_SEND_HAVES;
                }

                pckOut.WriteString("have " + c.getId().Name + "\n");
                havesSent++;
                havesSinceLastContinue++;

                if ((31 & havesSent) != 0)
                {
                    continue;
                }

                if (monitor.IsCancelled)
                    throw new CancelledException();

                pckOut.End();
                resultsPending++;

                if (havesSent == 32)
                {
                    continue;
                }

                for (; ; )
                {
                    PacketLineIn.AckNackResult anr = pckIn.readACK(ackId);

                    switch (anr)
                    {
                        case PacketLineIn.AckNackResult.NAK:
                            // More have lines are necessary to compute the
                            // pack on the remote side. Keep doing that.

                            resultsPending--;
                            goto END_READ_RESULT;

                        case PacketLineIn.AckNackResult.ACK:
                            // The remote side is happy and knows exactly what
                            // to send us. There is no further negotiation and
                            // we can break out immediately.

                            _multiAck = MultiAck.OFF;
                            resultsPending = 0;
                            receivedAck = true;
                            goto END_SEND_HAVES;

                        case PacketLineIn.AckNackResult.ACK_CONTINUE:
                        case PacketLineIn.AckNackResult.ACK_COMMON:
                        case PacketLineIn.AckNackResult.ACK_READY:
                            // The server knows this commit (ackId). We don't
                            // need to send any further along its ancestry, but
                            // we need to continue to talk about other parts of
                            // our local history.

                            MarkCommon(_walk.parseAny(ackId));
                            receivedAck = true;
                            receivedContinue = true;
                            havesSinceLastContinue = 0;
                            break;
                    }


                    if (monitor.IsCancelled)
                        throw new CancelledException();
                }

            END_READ_RESULT:
                if (receivedContinue && havesSinceLastContinue > MAX_HAVES)
                {
                    break;
                }
            }

        END_SEND_HAVES:

            if (monitor.IsCancelled)
                throw new CancelledException();
            pckOut.WriteString("done\n");
            pckOut.Flush();

            if (!receivedAck)
            {
                _multiAck = MultiAck.OFF;
                resultsPending++;
            }

            while (resultsPending > 0 || _multiAck != MultiAck.OFF)
            {
                PacketLineIn.AckNackResult anr = pckIn.readACK(ackId);
                resultsPending--;

                switch (anr)
                {
                    case PacketLineIn.AckNackResult.NAK:
                        // A NAK is a response to an end we queued earlier
                        // we eat it and look for another ACK/NAK message.
                        //
                        break;

                    case PacketLineIn.AckNackResult.ACK:
                        // A solitary ACK at this point means the remote won't
                        // speak anymore, but is going to send us a pack now.
                        //
                        goto END_READ_RESULT_2;

                    case PacketLineIn.AckNackResult.ACK_CONTINUE:
                    case PacketLineIn.AckNackResult.ACK_COMMON:
                    case PacketLineIn.AckNackResult.ACK_READY:
                        // We will expect a normal ACK to break out of the loop.
                        //
                        _multiAck = MultiAck.CONTINUE;
                        break;
                }

                if (monitor.IsCancelled)
                    throw new CancelledException();
            }

        END_READ_RESULT_2:
            ;
        }

        private class NegotiateBeginRevFilter : RevFilter, IDisposable
        {
            private readonly RevFlag _common;
            private readonly RevFlag _advertised;

            public NegotiateBeginRevFilter(RevFlag c, RevFlag a)
            {
                _common = c;
                _advertised = a;
            }

            public override RevFilter Clone()
            {
                return this;
            }

            public override bool include(GitSharp.Core.RevWalk.RevWalk walker, RevCommit cmit)
            {
                bool remoteKnowsIsCommon = cmit.has(_common);
                if (cmit.has(_advertised))
                {
                    cmit.add(_common);
                }
                return !remoteKnowsIsCommon;
            }

            public void Dispose()
            {
                _advertised.Dispose();
                _common.Dispose();
            }

        }

        private void NegotiateBegin()
        {
            _walk.resetRetain(REACHABLE, ADVERTISED);
            _walk.markStart(_reachableCommits);
            _walk.sort(RevSort.COMMIT_TIME_DESC);
            _walk.setRevFilter(new NegotiateBeginRevFilter(COMMON, ADVERTISED));
        }

        private void MarkRefsAdvertised()
        {
            foreach (Ref r in Refs)
            {
                MarkAdvertised(r.ObjectId);
                if (r.PeeledObjectId != null)
                    MarkAdvertised(r.PeeledObjectId);
            }
        }

        private void MarkAdvertised(AnyObjectId id)
        {
            try
            {
                _walk.parseAny(id).add(ADVERTISED);
            }
            catch (IOException)
            {

            }
        }

        private void MarkCommon(RevObject obj)
        {
            obj.add(COMMON);
            RevCommit oComm = (obj as RevCommit);
            if (oComm != null)
            {
                oComm.carry(COMMON);
            }
        }

        private void ReceivePack(ProgressMonitor monitor)
        {
            IndexPack ip = IndexPack.Create(local, _sideband ? pckIn.sideband(monitor) : inStream);
            ip.setFixThin(_thinPack);
            ip.setObjectChecking(transport.CheckFetchedObjects);
            ip.index(monitor);
            _packLock = ip.renameAndOpenPack(_lockMessage);
        }

        public override void Dispose()
        {
            _walk.Dispose();
            REACHABLE.Dispose();
            COMMON.Dispose();
            ADVERTISED.Dispose();
            _reachableCommits.Dispose();
            base.Dispose();
        }

    }
}