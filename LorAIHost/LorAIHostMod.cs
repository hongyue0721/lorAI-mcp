using System;
using System.IO;
using UnityEngine;

namespace LorAIHost
{
    public class LorAIHostMod : ModInitializer
    {
        public override void OnInitializeMod()
        {
            Debug.Log("[LorAI] Mod initializing...");
            try
            {
                // Create persistent host
                var host = new GameObject("LorAIHost");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<UpdateHook>();

                // Start HTTP server
                HttpServer.Start();

                // Initial state export
                var state = StateExporter.ExportFullState();
                StateExporter.SaveToFile(state, "full_state.json");

                Debug.Log("[LorAI] Initialization complete.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[LorAI] Init failed: " + ex);
            }
        }
    }
}
