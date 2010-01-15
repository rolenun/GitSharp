/*
 * Copyright (C) 2010, Dominique van de Vorle <dvdvorle@gmail.com>
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
using System.Linq;
using System.Text;
using System.ComponentModel.Composition;

namespace GitSharp.Commands
{
    [Export(typeof(IGitCommand))]
    public class CatfileCommand
        : AbstractCommand
    {

        public CatfileCommand() {
        }

        // note: the naming of command parameters is not following .NET conventions in favour of git command line parameter naming conventions.

        #region Properties / Options
        public List<string> Arguments { get; set; }
        /// <summary>
        /// Not implemented
        /// 
        /// Instead of the content, show the object type identified by
        /// <object>.
        /// 
        /// </summary>
        public bool T { get; set; }

        /// <summary>
        /// Not implemented
        /// 
        /// Instead of the content, show the object size identified by
        /// <object>.
        /// 
        /// </summary>
        public bool S { get; set; }

        /// <summary>
        /// Not implemented
        /// 
        /// Suppress all output; instead exit with zero status if <object>
        /// exists and is a valid object.
        /// 
        /// </summary>
        public string E { get; set; }

        /// <summary>
        /// Not implemented
        /// 
        /// Pretty-print the contents of <object> based on its type.
        /// 
        /// </summary>
        public string P { get; set; }

        /// <summary>
        /// Not implemented
        /// 
        /// Print the SHA1, type, size, and contents of each object provided on
        /// stdin. May not be combined with any other options or arguments.
        /// 
        /// </summary>
        public bool Batch { get; set; }

        /// <summary>
        /// Not implemented
        /// 
        /// Print the SHA1, type, and size of each object provided on stdin. May not
        /// be combined with any other options or arguments.
        /// </summary>
        public bool BatchCheck { get; set; }

        #endregion
        
        #region MEF Implementation

        public override string Name { get { return GetType().Name; } }

        public override string Version { get { return "1.0.0.0"; } }

        #endregion

        public override void Execute()
        {
            throw new NotImplementedException();
        }
    }
}
