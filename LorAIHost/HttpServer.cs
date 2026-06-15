using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace LorAIHost
{
    public static class HttpServer
    {
        public const int PORT = 17127;
        public static bool IsRunning => _running;
        public static int RequestCount => _reqCount;

        private static HttpListener _listener;
        private static Thread _thread;
        private static volatile bool _running;
        private static int _reqCount;
        private static string _staticDir;
        private static readonly Queue<PendingRequest> _queue = new Queue<PendingRequest>();
        private static readonly object _lock = new object();

        // ─── Lifecycle ────────────────────────────────────────────

        public static void Start()
        {
            _staticDir = Path.Combine(Application.dataPath, "Mods", "StaticDataExport", "output");
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:" + PORT + "/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true, Name = "LorAI-HTTP" };
                _thread.Start();
                Debug.Log("[LorAI] HTTP server started on port " + PORT);
            }
            catch (Exception ex)
            {
                _running = false;
                Debug.LogError("[LorAI] Failed to start HTTP server: " + ex.Message);
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
        }

        // ─── Background listener ─────────────────────────────────

        private static void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    HttpListenerContext ctx = _listener.GetContext();
                    Interlocked.Increment(ref _reqCount);
                    HandleRequest(ctx);
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!_running) break;
                    Debug.LogError("[LorAI] Listen error: " + ex.Message);
                }
            }
        }

        // ─── Route dispatch (runs on background thread) ──────────
        //
        // Immediate routes (no Unity API needed):
        //   GET  /health
        //   GET  /static
        //   GET  /static/{name}
        //   GET  /action-status
        //   OPTIONS (CORS preflight)
        //
        // Queued routes (need Unity main thread):
        //   GET  /state
        //   GET  /state/{layer}
        //   POST /action

        private static void HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
            string method = ctx.Request.HttpMethod;

            try
            {
                // ── CORS preflight ──
                if (method == "OPTIONS")
                {
                    ctx.Response.StatusCode = 204;
                    AddCors(ctx.Response);
                    ctx.Response.Close();
                    return;
                }

                // ── GET routes ──
                if (method == "GET")
                {
                    // Health check
                    if (path == "" || path == "/health")
                    {
                        RespondJson(ctx.Response, 200, new Dictionary<string, object>
                        {
                            { "status", "ok" },
                            { "version", "1.0.0" },
                            { "requests", _reqCount }
                        });
                        return;
                    }

                    // Full state → main thread
                    if (path == "/state")
                    {
                        Enqueue(ctx, method, path, null);
                        return;
                    }

                    // State layer → main thread
                    if (path.StartsWith("/state/"))
                    {
                        Enqueue(ctx, method, path, null);
                        return;
                    }

                    // Static file listing
                    if (path == "/static")
                    {
                        List<object> files = new List<object>();
                        if (Directory.Exists(_staticDir))
                        {
                            foreach (string f in Directory.GetFiles(_staticDir, "*.json"))
                            {
                                FileInfo info = new FileInfo(f);
                                files.Add(new Dictionary<string, object>
                                {
                                    { "name", Path.GetFileNameWithoutExtension(f) },
                                    { "size", info.Length }
                                });
                            }
                        }
                        RespondJson(ctx.Response, 200, new Dictionary<string, object>
                        {
                            { "files", files },
                            { "count", files.Count }
                        });
                        return;
                    }

                    // Static file content
                    if (path.StartsWith("/static/"))
                    {
                        string name = path.Substring("/static/".Length);
                        // Prevent path traversal
                        if (name.Contains("..") || name.Contains("/") || name.Contains("\\"))
                        {
                            RespondJson(ctx.Response, 400, Err("Invalid file name"));
                            return;
                        }
                        string filePath = Path.Combine(_staticDir, name + ".json");
                        if (File.Exists(filePath))
                        {
                            string content = File.ReadAllText(filePath, Encoding.UTF8);
                            ctx.Response.StatusCode = 200;
                            ctx.Response.ContentType = "application/json; charset=utf-8";
                            AddCors(ctx.Response);
                            byte[] bytes = Encoding.UTF8.GetBytes(content);
                            ctx.Response.ContentLength64 = bytes.Length;
                            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                            ctx.Response.Close();
                        }
                        else
                        {
                            RespondJson(ctx.Response, 404, Err("File not found: " + name));
                        }
                        return;
                    }

                    // Deferred action status
                    if (path == "/action-status")
                    {
                        RespondJson(ctx.Response, 200, UpdateHook.GetDeferredStatus());
                        return;
                    }

                    RespondJson(ctx.Response, 404,
                        Err("Not found. Try /health, /state, /state/{layer}, /static, /static/{name}, /action-status"));
                    return;
                }

                // ── POST routes ──
                if (method == "POST")
                {
                    if (path == "/action")
                    {
                        string body;
                        using (StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                        {
                            body = reader.ReadToEnd();
                        }
                        Enqueue(ctx, method, path, body);
                        return;
                    }

                    RespondJson(ctx.Response, 404, Err("Unknown POST endpoint. Only /action is supported."));
                    return;
                }

                // ── Unsupported method ──
                RespondJson(ctx.Response, 405, Err("Method not allowed"));
            }
            catch (Exception ex)
            {
                try { RespondJson(ctx.Response, 500, Err(ex.Message)); }
                catch { }
            }
        }

        // ─── Queue management ─────────────────────────────────────

        private static void Enqueue(HttpListenerContext ctx, string method, string path, string body)
        {
            PendingRequest pending = new PendingRequest(ctx)
            {
                Method = method,
                Path = path,
                Body = body
            };
            lock (_lock)
            {
                _queue.Enqueue(pending);
            }
        }

        /// <summary>
        /// Called from UpdateHook.Update() on the Unity main thread.
        /// Processes all queued requests that require game-API access.
        /// </summary>
        public static void ProcessQueue()
        {
            // Process at most 2 requests per frame to avoid frame spikes
            int processed = 0;
            while (processed < 2)
            {
                processed++;
                PendingRequest pending;
                lock (_lock)
                {
                    if (_queue.Count == 0) break;
                    pending = _queue.Dequeue();
                }

                try
                {
                    // ── GET /state ──
                    if (pending.Method == "GET" && pending.Path == "/state")
                    {
                        Dictionary<string, object> data = StateExporter.ExportFullState();
                        RespondJson(pending.Context.Response, 200, data);
                        continue;
                    }

                    // ── GET /state/{layer} ──
                    if (pending.Method == "GET" && pending.Path.StartsWith("/state/"))
                    {
                        string layer = pending.Path.Substring("/state/".Length);
                        object layerData = StateExporter.GetLayer(layer);
                        if (layerData != null)
                        {
                            RespondJson(pending.Context.Response, 200, layerData);
                        }
                        else
                        {
                            RespondJson(pending.Context.Response, 404, Err("Unknown layer: " + layer));
                        }
                        continue;
                    }

                    // ── POST /action ──
                    if (pending.Method == "POST" && pending.Path == "/action")
                    {
                        Dictionary<string, object> parsed = JsonHelper.Parse(pending.Body);

                        string action = "";
                        if (parsed != null && parsed.ContainsKey("action") && parsed["action"] is string a)
                        {
                            action = a;
                        }

                        Dictionary<string, object> result = ActionHandler.Execute(action, parsed);

                        // Deferred actions: don't respond yet, let ProcessDeferred handle it
                        if (result.ContainsKey("_deferred") && result["_deferred"] is bool d && d)
                        {
                            string reqId = result.ContainsKey("_deferredReqId")
                                ? result["_deferredReqId"].ToString()
                                : "deferred_" + Interlocked.Increment(ref UpdateHook._nextDeferredId);

                            DeferredAction deferred = new DeferredAction
                            {
                                Response = pending.Context.Response,
                                ReqId = reqId,
                                Routine = result.ContainsKey("_routine") ? result["_routine"] as IEnumerator : null,
                                StartTime = Time.time,
                                TimeoutSeconds = 30f,
                                ResultDict = result
                            };

                            lock (UpdateHook._deferLock)
                            {
                                UpdateHook._deferredActions.Add(deferred);
                            }
                        }
                        else
                        {
                            Dictionary<string, object> response = new Dictionary<string, object>
                            {
                                { "status", (result.ContainsKey("success") && result["success"] is bool s && s) ? "ok" : "error" },
                                { "action", parsed },
                                { "result", result }
                            };

                            // Attach current state snapshot (only for non-deferred)
                            try { response["state"] = StateExporter.ExportFullState(); }
                            catch (Exception ex) { response["stateError"] = ex.Message; }

                            RespondJson(pending.Context.Response, 200, response);
                        }
                        continue;
                    }

                    // ── Fallback ──
                    RespondJson(pending.Context.Response, 404, Err("Unknown queued request: " + pending.Path));
                }
                catch (Exception ex)
                {
                    try { RespondJson(pending.Context.Response, 400, Err("Action failed: " + ex.Message)); }
                    catch { }
                }
            }
        }

        // ─── Response helpers ─────────────────────────────────────

        private static void RespondJson(HttpListenerResponse resp, int status, object data)
        {
            resp.StatusCode = status;
            resp.ContentType = "application/json; charset=utf-8";
            AddCors(resp);
            string json = JsonHelper.Serialize(data);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentLength64 = bytes.Length;
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.Close();
        }

        private static void AddCors(HttpListenerResponse resp)
        {
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");
        }

        private static Dictionary<string, object> Err(string msg)
        {
            return new Dictionary<string, object> { { "error", msg } };
        }
    }

    // ─── Queued request model ─────────────────────────────────────

    internal class PendingRequest
    {
        public HttpListenerContext Context;
        public string Method;
        public string Path;
        public string Body;
        public Dictionary<string, object> ParsedBody = null;

        public PendingRequest(HttpListenerContext ctx)
        {
            Context = ctx;
            Method = ctx.Request.HttpMethod;
            Path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
        }
    }
}
