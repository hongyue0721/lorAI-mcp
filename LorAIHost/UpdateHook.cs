using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using UnityEngine;

namespace LorAIHost
{
    public class UpdateHook : MonoBehaviour
    {
        // ─── Deferred action tracking ─────────────────────────────

        internal static List<DeferredAction> _deferredActions = new List<DeferredAction>();
        internal static Dictionary<string, Dictionary<string, object>> _deferredResults =
            new Dictionary<string, Dictionary<string, object>>();
        internal static int _nextDeferredId = 1;
        internal static readonly object _deferLock = new object();

        // ─── Unity callbacks ──────────────────────────────────────

        public void Update()
        {
            HttpServer.ProcessQueue();
            ProcessDeferred();
        }

        public void OnGUI()
        {
            GUI.Label(
                new Rect(Screen.width - 260, 5, 255, 20),
                HttpServer.IsRunning
                    ? "[LorAI] :" + HttpServer.PORT + " | " + HttpServer.RequestCount + " req"
                    : "[LorAI] STOPPED"
            );
        }

        // ─── Deferred action processing ───────────────────────────
        //
        // Each frame, drive any active coroutines forward and check
        // whether results have been posted externally.  On completion
        // (or timeout) send the HTTP response and archive the result.

        public static void ProcessDeferred()
        {
            List<DeferredAction> snapshot;
            lock (_deferLock)
            {
                if (_deferredActions.Count == 0) return;
                snapshot = new List<DeferredAction>(_deferredActions);
            }

            foreach (DeferredAction item in snapshot)
            {
                bool done = false;
                string reqId = item.ReqId;

                // Drive coroutine forward by one step
                if (item.Routine != null)
                {
                    try
                    {
                        if (!item.Routine.MoveNext())
                        {
                            // Coroutine finished
                            done = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (item.ResultDict != null)
                        {
                            item.ResultDict["success"] = false;
                            item.ResultDict["message"] = "Deferred error: " + ex.Message;
                        }
                        done = true;
                    }
                }
                else
                {
                    // No routine — check if an external hook posted a result
                    bool hasResult;
                    lock (_deferLock)
                    {
                        hasResult = _deferredResults.ContainsKey(reqId);
                    }
                    if (hasResult)
                    {
                        done = true;
                    }
                }

                // Timeout guard
                if (!done && (Time.time - item.StartTime) >= item.TimeoutSeconds)
                {
                    done = true;
                    if (item.ResultDict != null)
                    {
                        item.ResultDict["success"] = false;
                        item.ResultDict["message"] = "Deferred action timed out after " + item.TimeoutSeconds + "s";
                    }
                }

                if (!done) continue;

                // ── Completion ──

                // Remove from active list
                lock (_deferLock)
                {
                    _deferredActions.Remove(item);
                }

                // Resolve final result data
                Dictionary<string, object> resultData;
                lock (_deferLock)
                {
                    if (_deferredResults.ContainsKey(reqId))
                    {
                        resultData = _deferredResults[reqId];
                    }
                    else
                    {
                        resultData = item.ResultDict != null
                            ? item.ResultDict
                            : new Dictionary<string, object> { { "success", false }, { "message", "No result" } };
                    }
                }

                // Build response payload
                Dictionary<string, object> response = new Dictionary<string, object>
                {
                    {
                        "status",
                        (resultData.ContainsKey("success") && resultData["success"] is bool s && s) ? "ok" : "error"
                    },
                    { "result", resultData }
                };

                try { response["state"] = StateExporter.ExportFullState(); }
                catch (Exception ex) { Debug.LogWarning("[LorAI] Deferred state export failed: " + ex.Message); }

                // Send HTTP response
                try
                {
                    RespondDeferred(item.Response, 200, response);
                }
                catch (Exception ex) { Debug.LogWarning("[LorAI] Deferred response failed: " + ex.Message); }

                // Archive result for /action-status polling (cap at 50 to prevent leak)
                lock (_deferLock)
                {
                    _deferredResults[reqId] = resultData;
                    if (_deferredResults.Count > 50)
                    {
                        // Remove oldest entries (first added)
                        var oldest = _deferredResults.Keys.First();
                        _deferredResults.Remove(oldest);
                    }
                }
            }
        }

        // ─── Deferred status for /action-status endpoint ──────────

        public static Dictionary<string, object> GetDeferredStatus()
        {
            Dictionary<string, object> completed = new Dictionary<string, object>();
            List<string> pending = new List<string>();

            lock (_deferLock)
            {
                foreach (KeyValuePair<string, Dictionary<string, object>> kv in _deferredResults)
                {
                    completed[kv.Key] = kv.Value;
                }
                foreach (DeferredAction item in _deferredActions)
                {
                    pending.Add(item.ReqId);
                }
            }

            return new Dictionary<string, object>
            {
                { "completed", completed },
                { "pending", pending }
            };
        }

        // ─── Internal response helper for deferred actions ────────

        private static void RespondDeferred(HttpListenerResponse resp, int status, object data)
        {
            resp.StatusCode = status;
            resp.ContentType = "application/json; charset=utf-8";
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");
            string json = JsonHelper.Serialize(data);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.Close();
        }
    }

    // ─── Deferred action struct ───────────────────────────────────

    internal struct DeferredAction
    {
        public HttpListenerResponse Response;
        public string ReqId;
        public IEnumerator Routine;
        public float StartTime;
        public float TimeoutSeconds;
        public Dictionary<string, object> ResultDict;
    }
}
