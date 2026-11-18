#if !NATIVE_ONLY
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace OverlayHUD
{
    public sealed partial class Plugin
    {
        private IEnumerator SyncIconAppearance()
        {
            // Re-send after server restarts; the endpoint only broadcasts changes.
            while (true)
            {
                string url = BuildSiblingEndpoint(levelEndpoint.Value, "/api/appearance");
                string json = "{\"legacyIcons\":" + (legacyIcons.Value ? "true" : "false") + "}";
                Task<string> request = QueueNetworkRequest(() => SendHttpPost(url, json));
                while (!request.IsCompleted) yield return null;
                // Observe failures and retry when the bundled server becomes available.
                if (request.IsFaulted) { var error = request.Exception; }
                yield return new WaitForSecondsRealtime(2f);
            }
        }
    }
}
#endif
