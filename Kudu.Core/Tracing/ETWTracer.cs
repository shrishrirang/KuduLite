using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Text;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;

namespace Kudu.Core.Tracing
{
    public class ETWTracer : ITracer
    {
        // log REST api
        private readonly string _requestId;
        private readonly string _requestMethod;

        public ETWTracer(string requestId, string requestMethod)
        {
            // a new ETWTracer per request
            _requestId = requestId;
            _requestMethod = requestMethod;
        }

        // traceLevel does NOT YET apply to ETWTracer
        public TraceLevel TraceLevel
        {
            get
            {
                return TraceLevel.Verbose;
            }
        }

        public IDisposable Step(string message, IDictionary<string, string> attributes)
        {
            Trace(message, attributes);

            return DisposableAction.Noop;
        }

        public void Trace(string message, IDictionary<string, string> attributes)
        {
            // these are traced by TraceModule
            if (message == XmlTracer.IncomingRequestTrace ||
                message == XmlTracer.OutgoingResponseTrace)
            {
                return;
            }

            string type = null;
            string text = null;
            if (_requestMethod == HttpMethod.Get.Method)
            {
                // ignore normal GET request body
                if (!attributes.TryGetValue("type", out type))
                {
                    return;
                }

                if (type != "error" && type != "warning")
                {
                    return;
                }
            }

            attributes.TryGetValue("text", out text);
           
            var strb = new StringBuilder();
            strb.AppendFormat("{0} ", message);

            // took from XMLtracer
            foreach (var attrib in attributes)
            {
                if (TraceExtensions.IsNonDisplayableAttribute(attrib.Key))
                {
                    continue;
                }

                strb.AppendFormat("{0}=\"{1}\" ", attrib.Key, attrib.Value);
            }
            if (type == "error")
            {
                KuduEventGenerator.Log().KuduException(
                    ServerConfiguration.GetApplicationName(),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    text,
                    strb.ToString());
            }
            else
            {
                KuduEventGenerator.Log().GenericEvent(ServerConfiguration.GetApplicationName(),
                                             strb.ToString(),
                                             _requestId,
                                             string.Empty,
                                             string.Empty,
                                             Constants.KuduBuild);
            }
        }
    }
}