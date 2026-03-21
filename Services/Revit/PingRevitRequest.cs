using Autodesk.Revit.UI;
using Renumber.Services.Core;

namespace Renumber.Services.Revit
{
    /// <summary>
    /// Sample request to verify external event wiring.
    /// </summary>
    public class PingRevitRequest : IExternalEventRequest
    {
        private readonly ILogger _logger;

        public PingRevitRequest(ILogger logger)
        {
            _logger = logger;
        }

        public void Execute(UIApplication app)
        {
            if (app.ActiveUIDocument != null)
            {
                string title = app.ActiveUIDocument.Document.Title;
                _logger.Info($"Pong: {title} (Thread ID: {System.Threading.Thread.CurrentThread.ManagedThreadId})");
            }
            else
            {
                _logger.Warning("Pong: No active document.");
            }
        }
    }
}
