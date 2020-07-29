using Fiddler;
using Fiddler.WebFormats;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WPRImport
{
    class WPRImporter
    {

        internal static string DescribeExceptionWithStack(Exception eX)
        {
            StringBuilder oSB = new StringBuilder(512);
            oSB.AppendLine(eX.Message);
            oSB.AppendLine(eX.StackTrace);
            if (null != eX.InnerException)
            {
                oSB.AppendFormat(" < {0}", eX.InnerException.Message);
            }
            return oSB.ToString();
        }

        List<Session> _listSessions;
        readonly EventHandler<ProgressCallbackEventArgs> _evtProgressNotifications;

        internal WPRImporter(StreamReader oSR, List<Session> listSessions, EventHandler<ProgressCallbackEventArgs> evtProgressNotifications)
        {
            _listSessions = listSessions;
            _evtProgressNotifications = evtProgressNotifications;
            Stopwatch oSW = Stopwatch.StartNew();
            Hashtable htFile = JSON.JsonDecode(oSR.ReadToEnd(), out _) as Hashtable;
            if (null == htFile)
            {
                NotifyProgress(1.00f, "Aborting; file is not a properly-formatted WPR Capture.");
                FiddlerApplication.DoNotifyUser("This file is not a properly-formatted WPR Capture.", "Import aborted");
                return;
            }

            NotifyProgress(0.25f, "Finished parsing JSON file; took " + oSW.ElapsedMilliseconds + "ms.");
            if (!ExtractSessionsFromJSON(htFile))
            {
                FiddlerApplication.DoNotifyUser("This JSON file does not seem to contain WPR Capture data.", "Unexpected Data");
                Session sessFile = Session.BuildFromData(false,
                    new HTTPRequestHeaders(
                        String.Format("/file.json"),
                        new[] { "Host: failed-import", "Date: " + DateTime.UtcNow.ToString() }),
                    Utilities.emptyByteArray,
                    new HTTPResponseHeaders(200, "File Data", new[] { "Content-Type: application/json; charset=utf-8" }),
                    Encoding.UTF8.GetBytes(JSON.JsonEncode(htFile)),
                    SessionFlags.ImportedFromOtherTool | SessionFlags.RequestGeneratedByFiddler | SessionFlags.ResponseGeneratedByFiddler | SessionFlags.ServedFromCache);
                listSessions.Insert(0, sessFile);
            }
        }

        private void NotifyProgress(float fPct, string sMessage)
        {
            _evtProgressNotifications?.Invoke(null, new ProgressCallbackEventArgs(fPct, sMessage));
        }

        private int getIntValue(object oValue, int iDefault)
        {
            if (null == oValue) return iDefault;
            if (!(oValue is Double)) return iDefault;
            return (int)(double)oValue;
        }

        public bool ExtractSessionsFromJSON(Hashtable htFile)
        {
            if (!(htFile["Requests"] is Hashtable htRequests)) return false;
            //if (!(htFile["Certs"] is Hashtable htCerts)) return false;
            //if (!(htFile["NegotiatedProtocol"] is Hashtable htProtocol)) return false;


            NotifyProgress(0, "Found WPR Capture data.");

            // Create a Summary Session, the response body of which we'll fill in later.
           /* Session sessSummary = Session.BuildFromData(false,
                    new HTTPRequestHeaders(
                        String.Format("/CAPTURE_INFO"), // TODO: Add Machine name?
                        new[] { "Host: NETLOG" , "Date: " + dtBase.ToString("r") }),
                    Utilities.emptyByteArray,
                    new HTTPResponseHeaders(200, "Analyzed Data", new[] { "Content-Type: text/plain; charset=utf-8" }),
                    Utilities.emptyByteArray,
                    SessionFlags.ImportedFromOtherTool | SessionFlags.RequestGeneratedByFiddler | SessionFlags.ResponseGeneratedByFiddler | SessionFlags.ServedFromCache);
            _listSessions.Add(sessSummary);
            _listSessions.Add(Session.BuildFromData(false,
                new HTTPRequestHeaders(
                    String.Format("/RAW_JSON"), // TODO: Add Machine name?
                    new[] { "Host: NETLOG" }),
                Utilities.emptyByteArray,
                new HTTPResponseHeaders(200, "Analyzed Data", new[] { "Content-Type: application/json; charset=utf-8" }),
                Encoding.UTF8.GetBytes(JSON.JsonEncode(htFile)),
                SessionFlags.ImportedFromOtherTool | SessionFlags.RequestGeneratedByFiddler | SessionFlags.ResponseGeneratedByFiddler | SessionFlags.ServedFromCache));
            */

            foreach (Hashtable htURLs in htRequests.Values)
            {
                foreach (DictionaryEntry de in htURLs)
                {
                    ArrayList alPairs = de.Value as ArrayList;
                    string sURL = (de.Key as String);
                    bool bIsHTTPS = sURL.StartsWith("https:");
                    // FiddlerApplication.Log.LogFormat("Parsing requests/responses for {0}", sURL);

                    // Each request/response pair instance for this URL
                    foreach (Hashtable htPair in alPairs)
                    {
                        try
                        {
                            string sRequest = htPair["SerializedRequest"] as String;
                            string sResponse = htPair["SerializedResponse"] as String;

                            byte[] arrRequest = Convert.FromBase64String(sRequest);
                            byte[] arrResponse = Convert.FromBase64String(sResponse);
                            // FiddlerApplication.Log.LogString(Utilities.ByteArrayToHexView(arrRequest, 16));

                            Session oNewSession = new Session(arrRequest, arrResponse, SessionFlags.ImportedFromOtherTool);

                            // If the capture URL indicates HTTPS, update the Session URL to HTTPS.
                            // (Request Header bytes only contain the scheme when the request was known to be directed at a proxy)
                            if (bIsHTTPS && oNewSession.fullUrl != sURL)
                            {
                                oNewSession.RequestHeaders.UriScheme = "https";
                                oNewSession.UNSTABLE_SetBitFlag(SessionFlags.IsHTTPS, true);

                                // The URLs should now exactly match.
                                Debug.Assert(oNewSession.fullUrl == sURL);
                            }
                            _listSessions.Add(oNewSession);
                        }
                        catch (Exception eX)
                        {
                            NotifyProgress(0, DescribeExceptionWithStack(eX));
                        }
                    }
                }
                /*
                int iPct = (int)(100 * (0.25f + 0.50f * (iEvent / (float)cEvents)));
                if (iPct != iLastPct)
                {
                    NotifyProgress(iPct / 100f, "Parsed an event for a URLRequest");
                    iLastPct = iPct;
                }*/
            }
            NotifyProgress(1.0f, "Import completed; saw " + _listSessions.Count.ToString() + " requests");
            return true;
        }
    }
}
