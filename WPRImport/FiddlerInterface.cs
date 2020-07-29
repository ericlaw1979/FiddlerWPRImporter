using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Fiddler;

namespace WPRImport
{
    [ProfferFormat("WPRCapture JSON", "WebPageReplay Capture (v1.0.1). See https://github.com/catapult-project/catapult/blob/master/web_page_replay_go/README.md for more details.")]
    public class HTTPArchiveFormatImport : ISessionImporter
    {
        public Session[] ImportSessions(string sFormat, Dictionary<string, object> dictOptions, EventHandler<Fiddler.ProgressCallbackEventArgs> evtProgressNotifications)
        {
            if ((sFormat != "WPRCapture JSON")) { Debug.Assert(false); return null; }

            MemoryStream strmContent = null;
            string sFilename = null;
            if (null != dictOptions)
            {
                if (dictOptions.ContainsKey("Filename"))
                {
                    sFilename = dictOptions["Filename"] as string;
                }
                else if (dictOptions.ContainsKey("Content"))
                {
                    strmContent = new MemoryStream(Encoding.UTF8.GetBytes(dictOptions["Content"] as string));
                }
            }

            if ((null == strmContent) && string.IsNullOrEmpty(sFilename))
            {
                sFilename = Fiddler.Utilities.ObtainOpenFilename("Import " + sFormat, "WPRGo JSON (*.wprgo)|*.wprgo");
            }

            if ((null != strmContent) || !String.IsNullOrEmpty(sFilename))
            {
                try
                {
                    List<Session> listSessions = new List<Session>();
                    StreamReader oSR;

                    if (null != strmContent)
                    {
                        oSR = new StreamReader(strmContent);
                    }
                    else
                    {
                        Stream oFS = File.OpenRead(sFilename);

                        // Check to see if this file data was GZIP'd. It SHOULD be.
                        bool bWasGZIP = false;
                        int bFirst = oFS.ReadByte();
                        if (bFirst == 0x1f && oFS.ReadByte() == 0x8b)
                        {
                            bWasGZIP = true;
                            evtProgressNotifications?.Invoke(null, new ProgressCallbackEventArgs(0, "Import file was compressed using gzip/DEFLATE."));
                        }

                        oFS.Position = 0;
                        if (bWasGZIP)
                        {
                            oFS = GetUnzippedBytes(oFS);
                        }

                        oSR = new StreamReader(oFS, Encoding.UTF8);
                    }

                    using (oSR)
                    {
                        new WPRImporter(oSR, listSessions, evtProgressNotifications);
                    }
                    return listSessions.ToArray();
                }
                catch (Exception eX)
                {
                    FiddlerApplication.ReportException(eX, "Failed to import NetLog");
                    return null;
                }
            }
            return null;
        }

        /// <summary>
        /// Read the all bytes of the supplied DEFLATE-compressed file and return a memorystream containing the expanded bytes.
        /// </summary>
        private MemoryStream GetUnzippedBytes(Stream oFS)
        {
            long fileLength = oFS.Length;
            if (fileLength > Int32.MaxValue)
                throw new IOException("file over 2gb");

            int index = 0;
            int count = (int)fileLength;
            byte[] bytes = new byte[count];

            while (count > 0)
            {
                int n = oFS.Read(bytes, index, count);
                index += n;
                count -= n;
            }

            return new MemoryStream(Utilities.GzipExpand(bytes));
        }

        public void Dispose() { }
    }
}
